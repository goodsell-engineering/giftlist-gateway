using System.Net;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.IntegrationTests.Platform;

/// <summary>
/// GL-44/GL-109/GL-113: the IDE, <c>?sdl</c> and introspection are one policy
/// (<c>GatewayGraphQlEndpointRouteBuilderExtensions.MapGatewayGraphQlEndpoints</c> and
/// <c>GatewayInfrastructureServiceCollectionExtensions.AddGraphQl</c> — see their own remarks for
/// the decision itself), on unconditionally in Development and off everywhere else. Every other
/// suite in this project runs against <see cref="GatewayFixture"/>'s own host, which is pinned to
/// Development — proving the "everywhere else" half needs a second host, built here against a
/// different <c>ASPNETCORE_ENVIRONMENT</c> but the same running Mongo/RabbitMQ (the connection
/// strings <see cref="GatewayFixture.InitializeAsync"/> already put in process-wide environment
/// variables, which <see cref="WebApplication.CreateBuilder"/> reads regardless of which
/// <c>WebApplicationFactory</c> built the host), rather than merely asserted from reading the code.
///
/// The "on in Development" half is pinned here too
/// (<see cref="GraphQlEndpoint_ShouldServeTheWholeSchema_WhenInDevelopment"/>), against the
/// fixture's own host: without it, the Production assertions are one-directional and a schema
/// endpoint that never worked at all would satisfy them.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class GraphQlSchemaExposureTests(GatewayFixture gateway) : IAsyncLifetime, IDisposable
{
    private WebApplicationFactory<Program> _productionFactory = null!;
    private HttpClient _productionClient = null!;

    public Task InitializeAsync()
    {
        _productionFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
        _productionClient = _productionFactory.CreateClient();
        return gateway.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _productionClient.Dispose();
        _productionFactory.Dispose();
    }

    [Fact]
    public async Task GraphQlEndpoint_ShouldNotServeTheIde_WhenNotInDevelopment()
    {
        // Arrange — the exact request GraphQlEndpoint_ShouldStillServeTheIde_WhenBrowsedWithoutAQuery
        // (GraphQlGetRequestTests) proves succeeds against the Development host.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/graphql");
        request.Headers.Accept.ParseAdd("text/html");

        // Act
        using var response = await _productionClient.SendAsync(request);

        // Assert — not the 200-OK-with-an-HTML-body shape the IDE responds with; this is the one
        // fact GraphQLServerOptions.Tool.Enable actually controls, asserted without depending on
        // exactly which non-200 answer HotChocolate gives a disabled tool.
        var servedTheIde = response.StatusCode == HttpStatusCode.OK
            && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.Ordinal);
        Assert.False(servedTheIde);
    }

    [Fact]
    public async Task GraphQlEndpoint_ShouldRejectSchemaRequests_WhenNotInDevelopment()
    {
        // Act — GL-113's own request shape: GET /graphql?sdl, no credential.
        using var response = await _productionClient.GetAsync("/graphql?sdl");

        // Assert
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GraphQlEndpoint_ShouldServeTheWholeSchema_WhenInDevelopment()
    {
        // Arrange — the SAME request as GraphQlEndpoint_ShouldRejectSchemaRequests_WhenNotInDevelopment,
        // against the Development host. THIS TEST IS WHY THAT ONE MEANS ANYTHING: its assertion is
        // NotEqual(OK), which a 404 from a misspelled path, a broken host or an `?sdl` HotChocolate
        // had never implemented would satisfy just as well. Pinning that the request DOES return
        // the schema when the policy says it should is what makes the pair falsifiable — flip
        // EnableSchemaRequests to a constant either way and exactly one of the two goes red.
        //
        // It also states the demo's real exposure as a fact rather than a reading of compose:
        // devenv runs as Development (DOTNET_ENVIRONMENT in its x-dotnet-env anchor), so this is
        // what the running stack serves an unauthenticated caller — GL-113's finding, kept
        // deliberately for a laptop demo.

        // Act — GL-113's own request shape: GET /graphql?sdl, no credential.
        using var response = await gateway.GraphQlHttpClient.GetAsync("/graphql?sdl");

        // Assert — 200 with the SDL itself, not merely a non-error: the body must carry the schema
        // so this cannot pass against an empty or unrelated 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sdl = await response.Content.ReadAsStringAsync();
        Assert.Contains("type Query", sdl, StringComparison.Ordinal);
        Assert.Contains("sharedGiftList", sdl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphQlEndpoint_ShouldRejectIntrospection_WhenNotInDevelopment()
    {
        // Arrange
        const string introspectionQuery = "{ __schema { queryType { name } } }";

        // Act
        var result = await GraphQlClient.QueryAsync(_productionClient, introspectionQuery);

        // Assert — HotChocolate.Types' DisableIntrospection turns this into a document-validation
        // error, not a transport-level rejection, so the response is still 200 with an `errors`
        // entry and no queryType data — asserted on the parsed GraphQL response, not the raw HTTP
        // status, since 200 here is expected and correct.
        Assert.Null(result.Data);
        Assert.NotEmpty(result.Errors);
    }
}
