using System.Text.Json;
using Gateway.IntegrationTests.Fixtures;
using GiftLists.Contracts.GiftLists.Events;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// GL-37: the one piece of Arrange <c>ReservationsGrpcServiceTests</c> and
/// <c>ReleaseSecretPrivacyTests</c> both need — a real, not-expired list with one item, reachable
/// by the share token a guest would actually hold, built from real <c>GiftLists.Contracts</c>
/// events on the real broker (CONVENTIONS.md "Testing"), the same way
/// <c>Reservations.IntegrationTests.Reservations.ReserveGiftTests.CreateReservableListAsync</c>
/// does on the other side of this bridge. Kept as a shared helper rather than one test class
/// exposing it to another.
/// </summary>
internal static class ReservableGiftLists
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public static async Task<(string ShareToken, Guid ItemId)> CreateAsync(GatewayFixture gateway)
    {
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        var itemId = Guid.NewGuid();
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, Guid.NewGuid(), "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego Set", null, null, DateTimeOffset.UtcNow));
        await Eventually.Async(
            () => GraphQlClient.QueryAsync(gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken }),
            response => response.Data is not null && response.Data.Value.GetProperty("sharedGiftList").ValueKind != JsonValueKind.Null,
            WaitTimeout);
        return (shareToken, itemId);
    }
}
