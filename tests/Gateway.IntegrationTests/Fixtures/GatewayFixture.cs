using BuildingBlocks.Messaging;
using BuildingBlocks.Testing;
using Gateway.Infrastructure.GiftLists.Grpc;
using Gateway.Infrastructure.Reservations.Grpc;
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
using Reservations.Contracts.Reservations.Events;

namespace Gateway.IntegrationTests.Fixtures;

/// <summary>
/// Builds the real Gateway composition root — an in-memory <c>WebApplicationFactory&lt;Program&gt;</c>
/// running the exact <c>Program.cs</c> pipeline (grpc-web middleware, GraphQL, CORS, auth,
/// AuthGrpcService) against the containers from <see cref="InfrastructureFixture"/> — plus a
/// "responder" Rebus host standing in for Identity (<see cref="FakeIdentityResponder"/>) and a
/// "publisher" one standing in for GiftLists and Reservations (<see cref="UpstreamEventPublisher"/>),
/// the only other services this one ever talks to in production. Tests enter through the real grpc-web/
/// GraphQL endpoints (CONVENTIONS.md "Testing"'s "entered at its real entry point"), never by calling
/// AuthGrpcService/an interactor directly.
/// </summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    /// <summary>Mirrors the internal <c>GiftListProjectionRepository.CollectionName</c> — not accessible from here, kept in sync by hand.</summary>
    public const string GiftListProjectionsCollectionName = "giftListProjections";

    /// <summary>Mirrors the internal <c>ReservationProjectionRepository.CollectionName</c> (GL-38) — same arrangement.</summary>
    public const string ReservationProjectionsCollectionName = "reservationProjections";

    private const string IdentityQueueName = "identity";

    /// <summary>
    /// Matches <see cref="Gateway.Infrastructure.Platform.GatewayMessageRouting.ReservationsQueueName"/>
    /// — kept as a local literal rather than a reference to that constant, the same way
    /// <see cref="IdentityQueueName"/> is (GL-109 review: not because Host/Contracts references
    /// are relevant here — this is a test project, not Host, and could reference the constant
    /// freely). A fake standing in for a far service on the real broker has to carry that
    /// service's own queue name independently of the code under test: if a reference to
    /// <c>GatewayMessageRouting.ReservationsQueueName</c> were used instead and that constant were
    /// ever changed to something wrong, this fixture would silently follow it to the same wrong
    /// queue and the test would still pass, catching nothing. A local literal is the one
    /// arrangement where a wrong <c>GatewayMessageRouting</c> constant actually fails a test.
    /// </summary>
    private const string ReservationsQueueName = "reservation";

    private const string DatabaseName = "gateway";

    /// <summary>Short enough that the reply-timeout test doesn't dominate the whole suite's runtime.</summary>
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);

    private readonly InfrastructureFixture _infrastructure = new();

    /// <summary>
    /// GL-37: every log entry the real Gateway host emits, so a test can prove a release secret
    /// never reaches a log line — the actual backstop this issue is about, not merely that a
    /// redacting value object exists (BuildingBlocks.Testing 0.2.0's own doc comment). Registered
    /// as the host's only provider (<c>ClearProviders</c> then <c>AddProvider</c>), same as that
    /// type's own remarks recommend.
    /// </summary>
    private readonly LogCapture _logCapture = new();

    private WebApplicationFactory<Program> _gatewayFactory = null!;
    private IHost _identityResponderHost = null!;
    private IHost _reservationsResponderHost = null!;
    private UpstreamEventPublisher _upstreamEventPublisher = null!;
    private GiftListsCommandListener _giftListsCommandListener = null!;

    public AuthService.AuthServiceClient AuthClient { get; private set; } = null!;

    /// <summary>GL-71: the grpc-web client for the GiftLists command surface.</summary>
    public GiftListsService.GiftListsServiceClient GiftListsClient { get; private set; } = null!;

    /// <summary>GL-37: the grpc-web client for the guest reservation surface.</summary>
    public ReservationsService.ReservationsServiceClient ReservationsClient { get; private set; } = null!;

    public HttpClient GraphQlHttpClient { get; private set; } = null!;

    public IMongoDatabase Database { get; private set; } = null!;

    /// <summary>GL-37: see <see cref="_logCapture"/>'s own doc comment.</summary>
    public LogCapture Logs => _logCapture;

    /// <summary>GL-73: see <see cref="GiftListsEventProbe"/>'s own doc comment.</summary>
    public GiftListsEventProbe EventProbe { get; private set; } = null!;

    /// <summary>The GiftLists integration events this fixture can publish onto the real broker — see <see cref="UpstreamEventPublisher"/>'s own doc comment.</summary>
    public Rebus.Bus.IBus GiftListsBus => _upstreamEventPublisher.Bus;

    /// <summary>GL-38: Reservations' <c>GiftReservedV1</c>, from the same publish-only host — see <see cref="UpstreamEventPublisher"/>'s own doc comment for why one host serves both.</summary>
    public Rebus.Bus.IBus ReservationsBus => _upstreamEventPublisher.Bus;

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

        // GL-37: stands in for Reservations on the real broker, same arrangement as the Identity
        // responder above — its own input queue must be the literal "reservation" name
        // (GatewayMessageRouting.ReservationsQueueName), not a throwaway one, since Rebus's
        // type-based routing addresses ReserveGift there by queue name, not by subscription.
        var reservationsBuilder = Host.CreateApplicationBuilder();
        reservationsBuilder.Logging.ClearProviders();
        reservationsBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RebusConfigurationExtensions.ConnectionStringConfigKey] = _infrastructure.RabbitMqConnectionString,
        });
        reservationsBuilder.Services.AddBuildingBlocksRebus(reservationsBuilder.Configuration, ReservationsQueueName);
        reservationsBuilder.Services.AddRebusHandler<FakeReservationsResponder>();
        _reservationsResponderHost = reservationsBuilder.Build();
        await _reservationsResponderHost.StartAsync();

        _upstreamEventPublisher = await UpstreamEventPublisher.StartAsync(_infrastructure.RabbitMqConnectionString);
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
            // GL-37: the only logging provider on the real Gateway host under test — see
            // _logCapture's own doc comment for why this is the actual proof a release secret
            // never reaches a log line.
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(_logCapture);
            });
            // GL-73: an extra handler per GiftLists event type, alongside (not instead of) the
            // production ones GatewayInfrastructureServiceCollectionExtensions.SubscribeToUpstreamEventsAsync wires up — Rebus
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
                services.AddRebusHandler<GiftListsEventProbeHandler<GiftReservedV1>>();
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
        ReservationsClient = new ReservationsService.ReservationsServiceClient(channel);

        GraphQlHttpClient = _gatewayFactory.CreateClient();
        Database = _gatewayFactory.Services.GetRequiredService<IMongoDatabase>();
        EventProbe = _gatewayFactory.Services.GetRequiredService<GiftListsEventProbe>();
    }

    public async Task DisposeAsync()
    {
        GraphQlHttpClient.Dispose();
        _gatewayFactory.Dispose();
        await _identityResponderHost.StopAsync();
        _identityResponderHost.Dispose();
        await _reservationsResponderHost.StopAsync();
        _reservationsResponderHost.Dispose();
        await _upstreamEventPublisher.DisposeAsync();
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
    /// unlike that one there are no unique indexes to re-apply — <c>ownerId</c>, <c>shareToken</c>
    /// and (GL-38) the reservation projection's <c>listId</c> are all non-unique, performance-only
    /// indexes here (none is a correctness requirement the way GiftLists' own unique
    /// <c>shareToken</c> index is; the reservation pair's uniqueness is its <c>_id</c>), so a test
    /// running before any exists would still pass, just via a collection scan.
    /// </summary>
    public Task ResetAsync() => Database.Client.DropDatabaseAsync(DatabaseName);

    /// <summary>
    /// A scope into the Gateway's own container (mirrors
    /// <c>GiftListsFixture.CreateGiftListsScope</c>'s own idea/naming) — needed because
    /// <c>IGiftListProjectionRepository</c> (and everything else registered by
    /// <c>AddGatewayInfrastructure</c>) is <c>Scoped</c>, so resolving it straight from
    /// <c>_gatewayFactory.Services</c> (the root provider) throws under ASP.NET Core's scope
    /// validation. Caller-disposed, one scope per test, same as GiftLists' own — nothing here
    /// needs a scope held open for the whole suite.
    /// </summary>
    public IServiceScope CreateGatewayScope() => _gatewayFactory.Services.CreateScope();

    /// <summary>
    /// GL-38: a WebSocket client into the in-memory host, for the subscription tests — the real
    /// <c>/graphql</c> endpoint over the real <c>graphql-transport-ws</c> protocol
    /// (<see cref="GraphQlSubscriptionClient"/>), not a resolver called directly.
    /// </summary>
    public Task<GraphQlSubscriptionClient> ConnectSubscriptionClientAsync(CancellationToken cancellationToken) =>
        GraphQlSubscriptionClient.ConnectAsync(_gatewayFactory.Server, cancellationToken);

    /// <summary>
    /// .NET's environment-variable configuration provider maps <c>Section:Key</c> to
    /// <c>Section__Key</c> (a colon isn't a legal env var name on every platform) — the same
    /// translation devenv's docker-compose.yml does by hand for the real host.
    /// </summary>
    private static void SetEnvironmentVariable(string configKey, string? value) =>
        Environment.SetEnvironmentVariable(configKey.Replace(":", "__", StringComparison.Ordinal), value);
}
