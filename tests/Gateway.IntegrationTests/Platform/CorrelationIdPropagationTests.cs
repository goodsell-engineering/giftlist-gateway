using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gateway.Infrastructure.GiftLists.Grpc;
using Gateway.Infrastructure.Platform;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Rebus.Messages;

namespace Gateway.IntegrationTests.Platform;

/// <summary>
/// GL-45: one user action traced end to end — a gRPC call carries a correlation id, that id is
/// stamped on the Rebus command it triggers, and the id survives back through a published
/// integration event into this Gateway's own logs and its projector — all the SAME id
/// (ARCHITECTURE.md "Cross-cutting concerns": "one user action is traceable end to end").
///
/// GiftLists itself is not run here (CONVENTIONS.md "Testing": "other services are not run"), so
/// the middle hop — a real GiftLists handler receiving that header, logging it, and republishing
/// it unchanged onto <c>GiftListCreatedV1</c> — cannot be exercised a second time from this repo;
/// it is proven for real, against GiftLists' own broker connection, by
/// <c>GiftLists.IntegrationTests.GiftLists.CorrelationIdPropagationTests</c>. What THIS test
/// proves, entirely for real: the Gateway's own ingress (<see cref="CorrelationIdMiddleware"/>)
/// actually seeds the id used on the wire, and the Gateway's own Rebus consumption
/// (<c>BuildingBlocks.Messaging.CorrelationId.CorrelationIdIncomingStep</c>, wired identically in
/// every host) actually surfaces it again once a matching event comes back — the id used for the
/// second half is the caller's own, not a fresh one, exactly as GiftLists' own propagation would
/// hand it back.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class CorrelationIdPropagationTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateGiftList_ShouldCarryTheCallersCorrelationId_ThroughTheRebusSendAndBackThroughAPublishedEvent()
    {
        // Arrange — one id for the whole user action, chosen by the caller (the SPA would do the
        // same with whatever it already uses to correlate its own network calls).
        var correlationId = $"trace-{Guid.NewGuid():N}";
        var ownerId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var request = new CreateGiftListRequest
        {
            Name = "Traced List",
            ExpiresAt = Timestamp.FromDateTimeOffset(expiresAt),
        };
        var headers = new Metadata
        {
            { "authorization", $"Bearer {TestTokenIssuer.IssueAccessToken(ownerId)}" },
            { CorrelationIdMiddleware.HeaderName, correlationId },
        };

        var logsBeforeEvent = gateway.Logs.Entries.Count;

        // Act 1 — the gRPC call, carrying the caller's id.
        var response = await gateway.GiftListsClient.CreateGiftListAsync(request, headers).ResponseAsync;
        var listId = Guid.Parse(response.ListId);

        // Assert 1 — the id CorrelationIdMiddleware read off the call is what actually rode the
        // Rebus Send this RPC triggered, not merely what the RPC claims to have sent.
        var sentCorrelationId = await Eventually.Async(
            () => Task.FromResult(gateway.GiftListsCommands.CorrelationIdForCreatedList(listId)),
            found => found is not null,
            WaitTimeout);
        Assert.Equal(correlationId, sentCorrelationId);

        // Act 2 — simulates GiftLists (not run here) having processed that command and published
        // GiftListCreatedV1 with the SAME header CorrelationIdOutgoingStep would have carried
        // forward from Act 1, unchanged, onto its own publish — the shared BuildingBlocks
        // plumbing this relies on is proven for real by GiftLists.IntegrationTests' own
        // CorrelationIdPropagationTests, against GiftLists' actual broker connection, not
        // reproduced here.
        await gateway.GiftListsBus.Publish(
            new GiftListCreatedV1(listId, ownerId, "Traced List", expiresAt, "TracedShareToken00001", DateTimeOffset.UtcNow),
            new Dictionary<string, string> { [Headers.CorrelationId] = correlationId });

        // Assert 2 — the Gateway's own consumption of that event logged the SAME id (its own
        // CorrelationIdIncomingStep hop) ...
        await Eventually.Async(
            () => Task.FromResult(gateway.Logs.Entries.Skip(logsBeforeEvent)
                .Any(e => e.Level == LogLevel.Information && e.Message.Contains(correlationId, StringComparison.Ordinal))),
            found => found,
            WaitTimeout);

        // ... and the Gateway's own projector actually applied it — queried through the real
        // /graphql endpoint (CONVENTIONS.md "Testing"), with the SAME id on this request too, and
        // echoed back on the response (CorrelationIdMiddleware, the ingress half of this hop).
        var (myGiftLists, echoedCorrelationId) = await Eventually.Async(
            () => QueryMyGiftListsAsync(ownerId, correlationId),
            result => result.MyGiftLists.EnumerateArray().Any(l => l.GetProperty("listId").GetGuid() == listId),
            WaitTimeout);

        Assert.Equal(correlationId, echoedCorrelationId);
        Assert.Contains(myGiftLists.EnumerateArray(), l => l.GetProperty("listId").GetGuid() == listId);
    }

    /// <summary>
    /// Bypasses <see cref="GraphQlClient"/> deliberately — that helper only ever returns the
    /// parsed body, and this test also needs the raw response headers to assert
    /// <see cref="CorrelationIdMiddleware"/>'s echo.
    /// </summary>
    private async Task<(JsonElement MyGiftLists, string? EchoedCorrelationId)> QueryMyGiftListsAsync(
        Guid ownerId, string correlationId)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query = GiftListGraphQlQueries.MyGiftLists }),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokenIssuer.IssueAccessToken(ownerId));
        httpRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        using var httpResponse = await gateway.GraphQlHttpClient.SendAsync(httpRequest);
        var echoed = httpResponse.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)
            ? values.SingleOrDefault()
            : null;

        var body = await httpResponse.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidOperationException("The GraphQL endpoint returned an empty body.");
        return (body.RootElement.GetProperty("data").GetProperty("myGiftLists"), echoed);
    }
}
