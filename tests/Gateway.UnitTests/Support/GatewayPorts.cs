using System.Security.Cryptography;
using Gateway.Application.Common;
using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.ViewGiftList;
using Gateway.Application.Reservations;
using Gateway.Infrastructure.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Gateway.UnitTests.Support;

/// <summary>
/// Resolves <c>ViewGiftList</c> through the same public <c>AddGatewayInfrastructure</c>
/// composition root <c>Gateway.Host</c> calls — CONVENTIONS.md "Reaching an internal from a test",
/// option 2: the interactor is <c>internal sealed</c>, but it is registered under a public
/// service type, <c>IInteractor&lt;ViewGiftListRequest, ViewGiftListResponse&gt;</c>, so no
/// <c>InternalsVisibleTo</c> grant is needed (mirrors
/// <c>Identity.UnitTests/Support/InfrastructurePorts.cs</c>). What comes back is the real
/// pipeline — <c>Validating&lt;,&gt;</c> then <c>Logging&lt;,&gt;</c> around the interactor —
/// so these tests also prove the wiring, which is the reason option 2 is preferred.
/// </summary>
/// <remarks>
/// The two projection ports are replaced with in-memory fakes <em>after</em> the composition root
/// has registered the real ones — Microsoft.Extensions.DependencyInjection resolves the last
/// registration for a service type — so nothing here touches Mongo, Rebus or HotChocolate. The
/// RSA key exists only to satisfy <c>AddGatewayInfrastructure</c>'s fail-fast JWT check; no token
/// is issued or validated by any test that uses this.
/// </remarks>
internal static class GatewayPorts
{
    public static IInteractor<ViewGiftListRequest, ViewGiftListResponse> BuildViewGiftList(
        FakeGiftListProjectionRepository giftLists,
        FakeReservationProjectionRepository reservations)
    {
        using var placeholderKey = RSA.Create(2048);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:PublicKeyPem"] = placeholderKey.ExportSubjectPublicKeyInfoPem(),
                ["Jwt:Issuer"] = "gateway-unit-tests",
                ["Jwt:Audience"] = "giftlist",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // GL-44: AddGatewayInfrastructure now takes an IHostEnvironment, to decide the
        // GraphQL IDE/introspection/schema-request policy (GatewayInfrastructureServiceCollectionExtensions.AddGraphQl) —
        // Development here for the same reason the placeholder RSA key exists: satisfying the
        // signature, not exercising GraphQL at all (nothing in this test builds or calls the
        // request executor).
        services.AddGatewayInfrastructure(configuration, new FakeHostEnvironment());
        services.AddScoped<IGiftListProjectionRepository>(_ => giftLists);
        services.AddScoped<IReservationProjectionRepository>(_ => reservations);

        var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<IInteractor<ViewGiftListRequest, ViewGiftListResponse>>();
    }

    /// <summary>Minimal <see cref="IHostEnvironment"/> — no ASP.NET Core host runs in this test, so nothing beyond <see cref="EnvironmentName"/> is ever read.</summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(GatewayPorts);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
