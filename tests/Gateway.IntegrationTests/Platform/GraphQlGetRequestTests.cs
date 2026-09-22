using System.Net;
using Gateway.Infrastructure.Platform;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;

namespace Gateway.IntegrationTests.Platform;

/// <summary>
/// GL-105: HotChocolate's own default (<c>EnableGetRequests = true</c>,
/// <c>AllowedGetOperations = Query</c>) lets any query — including <c>sharedGiftList(token)</c>,
/// whose <c>token</c> argument <em>is</em> the whole capability (ARCHITECTURE.md "Auth &amp;
/// sharing") — travel as a GET, putting the credential in the request line where reverse-proxy
/// access logs, browser history and any URL-logging intermediary can read it. Found live in the
/// Batch 34 review of GL-32, not inferred: the query below is the exact one demonstrated there.
///
/// Kept under <c>Platform/</c>, not <c>GiftLists/</c>, because the fix
/// (<see cref="GatewayGraphQlEndpointRouteBuilderExtensions"/>) is a transport-level policy that
/// would apply to any query the schema ever grows, not something particular to
/// <c>sharedGiftList</c> — <c>sharedGiftList</c> is simply the one query with a bearer-style
/// argument today, so it's what makes the leak demonstrable.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class GraphQlGetRequestTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SharedGiftList_ShouldBeRejected_WhenSentAsAnHttpGetWithTheTokenInTheQueryString()
    {
        // Arrange — a real, resolvable list and token: this must fail because the transport
        // refuses the GET, not merely because the token happens not to resolve to anything.
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, Guid.NewGuid(), "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await Eventually.Async(
            () => GraphQlClient.QueryAsync(
                gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken }),
            r => r.Data is not null,
            WaitTimeout);

        // Act — the same query, sent by GET with the token in the URL's query string, exactly as
        // reported: GET /graphql?query={sharedGiftList(token:"..."){listId name}}
        var query = $$"""{ sharedGiftList(token: "{{shareToken}}") { listId name } }""";
        var url = $"/graphql?query={Uri.EscapeDataString(query)}";
        using var response = await gateway.GraphQlHttpClient.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — refused at the HTTP method level, before HotChocolate ever parses the query
        // string into an operation (empirically 405 MethodNotAllowed, not a 400 GraphQL error
        // body — AllowedGetOperations.None removes GET as an operation-carrying verb for this
        // endpoint entirely, it does not merely reject the operation once parsed). In particular
        // the response never carries the list's name, which is what a GET-shaped leak would look
        // like.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.DoesNotContain("Birthday Wishlist", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Establishes what the fix does <em>not</em> break: a plain browser navigation to
    /// <c>/graphql</c> (no <c>query</c> parameter, <c>Accept: text/html</c>) still reaches
    /// HotChocolate's IDE, because <c>AllowedGetOperations.None</c> governs GET-as-a-query-
    /// transport, not the separate IDE-serving code path (<c>GraphQLServerOptions.Tool</c>,
    /// left at its default). Recorded as a fact this test pins, not merely a claim in the PR:
    /// the two settings are independent and this is the empirical proof, not an assumption.
    /// </summary>
    [Fact]
    public async Task GraphQlEndpoint_ShouldStillServeTheIde_WhenBrowsedWithoutAQuery()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/graphql");
        request.Headers.Accept.ParseAdd("text/html");

        // Act
        using var response = await gateway.GraphQlHttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — HTML, not a GraphQL error body; the IDE is unaffected by refusing GET queries.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// GL-109: the bug <see cref="SharedGiftList_ShouldBeRejected_WhenSentAsAnHttpGetWithTheTokenInTheQueryString"/>
    /// does not cover — a browser address bar sending the exact same GET-with-`query=` shape, but
    /// with an <c>Accept</c> header that prefers <c>text/html</c> (nobody's GraphQL client sends
    /// that; a browser always does). Before <c>UseGatewayGraphQlGetQueryGuard</c>, HotChocolate's
    /// content negotiation routed this toward the IDE-serving code path instead of
    /// <c>AllowedGetOperations.None</c>'s 405, and threw there — reported as a 500 with a
    /// developer-exception-page stack trace in Development, where this fixture runs
    /// (<c>GatewayFixture</c> pins <c>ASPNETCORE_ENVIRONMENT=Development</c>).
    /// </summary>
    [Fact]
    public async Task GraphQlGetWithQuery_ShouldBeRejected_WhenTheRequestPrefersHtml()
    {
        // Arrange — a trivial, always-valid query; this test is about the transport-level guard,
        // not about what the query itself would have returned.
        const string query = "{ __typename }";
        var url = $"/graphql?query={Uri.EscapeDataString(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html");

        // Act
        using var response = await gateway.GraphQlHttpClient.SendAsync(request);

        // Assert — the same deliberate 405 GL-105 already gives an API client for this shape of
        // request, not a 500: the guard short-circuits ahead of MapGraphQL() entirely, so the
        // Accept header this test sets never reaches HotChocolate's own content negotiation.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
