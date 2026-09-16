using BuildingBlocks.Messaging;
using Gateway.Application.GiftLists;
using Gateway.Infrastructure.GiftLists.Grpc;
using Gateway.Infrastructure.Users.Grpc;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Rebus.Config;

namespace Gateway.IntegrationTests.Fixtures;

/// <summary>
/// Builds the real Gateway composition root — an in-memory <c>WebApplicationFactory&lt;Program&gt;</c>
/// running the exact <c>Program.cs</c> pipeline (grpc-web middleware, GraphQL, CORS, auth,
/// AuthGrpcService) against the containers from <see cref="InfrastructureFixture"/> — plus a
/// "responder" Rebus host standing in for Identity (<see cref="FakeIdentityResponder"/>) and a
/// "publisher" one standing in for GiftLists (<see cref="GiftListsEventPublisher"/>), the only
/// other services this one ever talks to in production. Tests enter through the real grpc-web/
/// GraphQL endpoints (CONVENTIONS.md "Testing"'s "entered at its real entry point"), never by calling
/// AuthGrpcService/an interactor directly.
/// </summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    /// <summary>Mirrors the internal <c>GiftListProjectionRepository.CollectionName</c> — not accessible from here, kept in sync by hand.</summary>
    public const string GiftListProjectionsCollectionName = "giftListProjections";

    private const string IdentityQueueName = "identity";
    private const string DatabaseName = "gateway";

    /// <summary>Short enough that the reply-timeout test doesn't dominate the whole suite's runtime.</summary>
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);

    private readonly InfrastructureFixture _infrastructure = new();
    private WebApplicationFactory<Program> _gatewayFactory = null!;
    private IHost _identityResponderHost = null!;
    private GiftListsEventPublisher _giftListsEventPublisher = null!;
    private GiftListsCommandListener _giftListsCommandListener = null!;

    /// <summary>
    /// Kept open for the fixture's own lifetime (mirrors <c>GiftListsFixture.CreateGiftListsScope</c>'s
    /// own idea, but held rather than created per call) so <see cref="GiftListProjections"/> can be
    /// a plain property: <c>IGiftListProjectionRepository</c> is registered <c>Scoped</c>, so
    /// resolving it straight from <c>_gatewayFactory.Services</c> (the root provider) throws
    /// under ASP.NET Core's scope validation, the same way <c>EventProbe</c>/<c>Database</c> get
    /// away with it only because those two are <c>Singleton</c>. Safe to hold for the whole suite:
    /// the repository itself carries no per-request mutable state (CONVENTIONS.md "Persistence" —
    /// it wraps an <c>IMongoCollection</c> and a stateless retry policy).
    /// </summary>
    private IServiceScope _gatewayScope = null!;

    public AuthService.AuthServiceClient AuthClient { get; private set; } = null!;

    /// <summary>GL-71: the grpc-web client for the GiftLists command surface.</summary>
    public GiftListsService.GiftListsServiceClient GiftListsClient { get; private set; } = null!;

    public HttpClient GraphQlHttpClient { get; private set; } = null!;

    public IMongoDatabase Database { get; private set; } = null!;

    /// <summary>
    /// The real, composition-root-registered port (CONVENTIONS.md "Reaching an internal from a
    /// test" route 2 — resolved through the composition root, not a grant) — GL-31 needs to reach
    /// <see cref="IGiftListProjectionRepository.FindByShareTokenAsync"/> directly, since nothing
    /// public calls it yet (that is GL-32's job, not this one's).
    /// </summary>
    public IGiftListProjectionRepository GiftListProjections { get; private set; } = null!;

    /// <summary>GL-73: see <see cref="GiftListsEventProbe"/>'s own doc comment.</summary>
    public GiftListsEventProbe EventProbe { get; private set; } = null!;

    /// <summary>The GiftLists integration events this fixture can publish onto the real broker — see <see cref="GiftListsEventPublisher"/>'s own doc comment.</summary>
    public Rebus.Bus.IBus GiftListsBus => _giftListsEventPublisher.Bus;

    /// <summary>Every GiftLists command sent by <c>GiftListsGrpcService</c> (GL-71), recorded by <see cref="GiftListsCommandListener"/> standing in for GiftLists on the real broker.</summary>
    public GiftListsCommandSink GiftListsCommands => _giftListsCommandListener.Sink;

    public async Task InitializeAsync()
    {
        await _infrastructure.InitializeAsync();

        var identityBuilder = Host.CreateApplicationBuilder();
        identityBuilder.Logging.ClearProviders();
        identityBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RebusConfigurationExtensions.ConnectionStringConfigKey] = _infrastructure.RabbitMqConnectionString,
        });
        identityBuilder.Services.AddBuildingBlocksRebus(identityBuilder.Configuration, IdentityQueueName);
        identityBuilder.Services.AddRebusHandler<FakeIdentityResponder>();
        _identityResponderHost = identityBuilder.Build();
        await _identityResponderHost.StartAsync();

        _giftListsEventPublisher = await GiftListsEventPublisher.StartAsync(_infrastructure.RabbitMqConnectionString);
        _giftListsCommandListener = await GiftListsCommandListener.StartAsync(_infrastructure.RabbitMqConnectionString);

        // Program.cs reads configuration synchronously while building — before
        // WithWebHostBuilder's ConfigureAppConfiguration callback has a chance to run for a
        // minimal-hosting-model app (WebApplication.CreateBuilder), so the values have to be
        // there already. Environment variables are the one source WebApplication.CreateBuilder
        // always reads, and it's exactly how the real host gets its config too (devenv's
        // docker-compose.yml), so this exercises the same path production does.
        SetEnvironmentVariable(RebusConfigurationExtensions.ConnectionStringConfigKey, _infrastructure.RabbitMqConnectionString);
        SetEnvironmentVariable(
            RebusConfigurationExtensions.ReplyTimeoutSecondsConfigKey,
            ReplyTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetEnvironmentVariable("ConnectionStrings:Mongo", _infrastructure.MongoConnectionString);
        // GL-23: a fresh, test-process-only RSA key pair (TestJwtKeys) rather than the devenv dev
        // key — the Gateway validates with the public half, TestTokenIssuer signs with the private
        // half, and neither ever touches Identity's own signing key.
        SetEnvironmentVariable("Jwt:PublicKeyPem", TestJwtKeys.PublicKeyPem);
        SetEnvironmentVariable("Jwt:Issuer", TestJwtKeys.Issuer);
        SetEnvironmentVariable("Jwt:Audience", TestJwtKeys.Audience);

        _gatewayFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // GL-73: an extra handler per GiftLists event type, alongside (not instead of) the
            // production ones Program.cs's own SubscribeToGiftListsEventsAsync wires up — Rebus
            // runs every registered IHandleMessages<T> for a message and acks only once all of
            // them finish, so GiftListsEventProbe's count can only advance once the production
            // handler's own processing (successful or not) is also done.
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<GiftListsEventProbe>();
                services.AddRebusHandler<GiftListsEventProbeHandler<GiftListCreatedV1>>();
                services.AddRebusHandler<GiftListsEventProbeHandler<GiftListRenamedV1>>();
                services.AddRebusHandler<GiftListsEventProbeHandler<GiftListDeletedV1>>();
                services.AddRebusHandler<GiftListsEventProbeHandler<GiftItemAddedV1>>();
                services.AddRebusHandler<GiftListsEventProbeHandler<GiftItemRemovedV1>>();
            });
        });

        // Forces the host to build now rather than lazily on first request, so a startup failure
        // (e.g. a missing required config value) surfaces from InitializeAsync, not from inside
        // the first test that happens to run.
        var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, _gatewayFactory.Server.CreateHandler());
        var channel = GrpcChannel.ForAddress(_gatewayFactory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
        AuthClient = new AuthService.AuthServiceClient(channel);
        GiftListsClient = new GiftListsService.GiftListsServiceClient(channel);

        GraphQlHttpClient = _gatewayFactory.CreateClient();
        Database = _gatewayFactory.Services.GetRequiredService<IMongoDatabase>();
        EventProbe = _gatewayFactory.Services.GetRequiredService<GiftListsEventProbe>();
        _gatewayScope = _gatewayFactory.Services.CreateScope();
        GiftListProjections = _gatewayScope.ServiceProvider.GetRequiredService<IGiftListProjectionRepository>();
    }

    public async Task DisposeAsync()
    {
        GraphQlHttpClient.Dispose();
        _gatewayScope.Dispose();
        _gatewayFactory.Dispose();
        await _identityResponderHost.StopAsync();
        _identityResponderHost.Dispose();
        await _giftListsEventPublisher.DisposeAsync();
        await _giftListsCommandListener.DisposeAsync();
        await _infrastructure.DisposeAsync();

        SetEnvironmentVariable(RebusConfigurationExtensions.ConnectionStringConfigKey, null);
        SetEnvironmentVariable(RebusConfigurationExtensions.ReplyTimeoutSecondsConfigKey, null);
        SetEnvironmentVariable("ConnectionStrings:Mongo", null);
        SetEnvironmentVariable("Jwt:PublicKeyPem", null);
        SetEnvironmentVariable("Jwt:Issuer", null);
        SetEnvironmentVariable("Jwt:Audience", null);
    }

    /// <summary>
    /// CONVENTIONS.md "Testing": isolate by dropping the database between tests, never by restarting a
    /// container. Mirrors <c>GiftLists.IntegrationTests.Fixtures.GiftListsFixture.ResetAsync</c>;
    /// unlike that one there is no unique index to re-apply — the ownerId index is not a
    /// correctness requirement the way GiftLists' shareToken one is, only a performance one, so a
    /// test running before it exists would still pass, just via a collection scan.
    /// </summary>
    public Task ResetAsync() => Database.Client.DropDatabaseAsync(DatabaseName);

    /// <summary>
    /// .NET's environment-variable configuration provider maps <c>Section:Key</c> to
    /// <c>Section__Key</c> (a colon isn't a legal env var name on every platform) — the same
    /// translation devenv's docker-compose.yml does by hand for the real host.
    /// </summary>
    private static void SetEnvironmentVariable(string configKey, string? value) =>
        Environment.SetEnvironmentVariable(configKey.Replace(":", "__", StringComparison.Ordinal), value);
}
