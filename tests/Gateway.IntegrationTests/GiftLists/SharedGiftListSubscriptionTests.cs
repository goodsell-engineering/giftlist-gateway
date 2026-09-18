using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using Reservations.Contracts.Reservations.Events;

namespace Gateway.IntegrationTests.GiftLists;

/// <summary>
/// GL-38: <c>sharedGiftListChanged(token)</c>, the share-token-scoped realtime channel
/// (ARCHITECTURE.md "Realtime updates"), driven end to end — a real <c>GiftReservedV1</c> on the
/// real broker, a real <c>graphql-transport-ws</c> WebSocket into the real <c>/graphql</c>
/// endpoint (<see cref="GraphQlSubscriptionClient"/>), and the pushed payload asserted as
/// received. That there is no owner-facing subscription is proven by introspection in
/// <c>ReservationVisibilityTests</c>; this file is about the one that exists.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class SharedGiftListSubscriptionTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long a push that must NOT arrive is given to not arrive. See <see cref="SharedGiftListChanged_ShouldPushOnlyForItsOwnList_WhenAnItemOnAnotherListIsReserved"/> for why this is a floor, not the whole proof.</summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(2);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SharedGiftListChanged_ShouldPushTheGuestViewWithTheItemReserved_WhenAnItemOnThatListIsReserved()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        var itemId = Guid.NewGuid();
        await PublishListAsync(listId, shareToken, itemId);
        await WaitForItemAsync(shareToken, itemId);
        using var cancellation = new CancellationTokenSource(WaitTimeout);
        await using var client = await gateway.ConnectSubscriptionClientAsync(cancellation.Token);
        var id = await client.SubscribeAsync(GiftListGraphQlQueries.SharedGiftListChanged, new { token = shareToken }, cancellation.Token);

        // Act
        await gateway.ReservationsBus.Publish(new GiftReservedV1(listId, itemId, DateTimeOffset.UtcNow));
        var message = await client.ReceiveAsync(cancellation.Token);

        // Assert
        Assert.Equal("next", message.Type);
        Assert.Equal(id, message.Id);
        Assert.Empty(message.ErrorMessages);
        var view = message.Payload!.Value.GetProperty("data").GetProperty("sharedGiftListChanged");
        Assert.Equal(listId, view.GetProperty("listId").GetGuid());
        var item = Assert.Single(view.GetProperty("items").EnumerateArray());
        Assert.Equal(itemId, item.GetProperty("itemId").GetGuid());
        Assert.True(item.GetProperty("reserved").GetBoolean());
    }

    /// <summary>
    /// Share-token scoping. A subscriber to list A hears nothing when list B changes. Silence
    /// alone proves little — a broken channel is silent too — so after the window passes, A is
    /// changed and the very next message must be A's: the channel was live throughout, and B's
    /// push never entered it.
    /// </summary>
    [Fact]
    public async Task SharedGiftListChanged_ShouldPushOnlyForItsOwnList_WhenAnItemOnAnotherListIsReserved()
    {
        // Arrange — two lists, one subscriber
        var subscribedListId = Guid.NewGuid();
        var subscribedToken = ShareTokens.New();
        var subscribedItemId = Guid.NewGuid();
        await PublishListAsync(subscribedListId, subscribedToken, subscribedItemId);
        await WaitForItemAsync(subscribedToken, subscribedItemId);
        var otherListId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        await PublishListAsync(otherListId, ShareTokens.New(), otherItemId);
        using var cancellation = new CancellationTokenSource(WaitTimeout + SilenceWindow);
        await using var client = await gateway.ConnectSubscriptionClientAsync(cancellation.Token);
        await client.SubscribeAsync(GiftListGraphQlQueries.SharedGiftListChanged, new { token = subscribedToken }, cancellation.Token);

        // Act
        await gateway.ReservationsBus.Publish(new GiftReservedV1(otherListId, otherItemId, DateTimeOffset.UtcNow));
        var duringSilence = await client.TryReceiveAsync(SilenceWindow, cancellation.Token);
        await gateway.ReservationsBus.Publish(new GiftReservedV1(subscribedListId, subscribedItemId, DateTimeOffset.UtcNow));
        var afterOwnChange = await client.ReceiveAsync(cancellation.Token);

        // Assert
        Assert.Null(duringSilence);
        Assert.Equal("next", afterOwnChange.Type);
        var view = afterOwnChange.Payload!.Value.GetProperty("data").GetProperty("sharedGiftListChanged");
        Assert.Equal(subscribedListId, view.GetProperty("listId").GetGuid());
    }

    /// <summary>
    /// The pushed payload is the same type as the query's response, so the same fields are just
    /// as absent from it — asked for by name, rejected at subscribe time before any push could
    /// carry them (ARCHITECTURE.md "Realtime updates": "the pushed payload carries
    /// <c>reserved: boolean</c> only").
    /// </summary>
    [Fact]
    public async Task SharedGiftListChanged_ShouldHaveNoReservedAtOrReserverOrSecretField_WhenTheyAreAskedForByName()
    {
        // Arrange
        var shareToken = ShareTokens.New();
        var itemId = Guid.NewGuid();
        await PublishListAsync(Guid.NewGuid(), shareToken, itemId);
        await WaitForItemAsync(shareToken, itemId);
        using var cancellation = new CancellationTokenSource(WaitTimeout);
        await using var client = await gateway.ConnectSubscriptionClientAsync(cancellation.Token);

        // Act
        await client.SubscribeAsync(
            GiftListGraphQlQueries.SharedGiftListChangedReservationDetailFields, new { token = shareToken }, cancellation.Token);
        var message = await client.ReceiveAsync(cancellation.Token);

        // Assert
        Assert.Equal("error", message.Type);
        foreach (var field in new[] { "reservedAt", "reservedBy", "reservationId", "releaseSecret" })
        {
            Assert.Contains(message.ErrorMessages, m => m.Contains(field, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Same answers as the query, from the same interactor: a token that resolves to nothing is
    /// <c>gateway.not_found</c>, a malformed one <c>gateway.invalid_share_token</c> — and either
    /// way the subscription fails at subscribe time, so no topic is ever joined on a token that
    /// did not resolve.
    /// </summary>
    [Theory]
    [InlineData("aBcDeFgHiJkLmNoPqRsTu", "gateway.not_found")]
    [InlineData("share-abc", "gateway.invalid_share_token")]
    public async Task SharedGiftListChanged_ShouldFailAtSubscribeTime_WhenTheTokenDoesNotResolveToAList(string token, string expectedErrorCode)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource(WaitTimeout);
        await using var client = await gateway.ConnectSubscriptionClientAsync(cancellation.Token);

        // Act
        var id = await client.SubscribeAsync(GiftListGraphQlQueries.SharedGiftListChanged, new { token }, cancellation.Token);
        var message = await client.ReceiveAsync(cancellation.Token);

        // Assert
        Assert.Equal("error", message.Type);
        Assert.Equal(id, message.Id);
        Assert.Equal([expectedErrorCode], message.ErrorCodes);
    }

    private async Task PublishListAsync(Guid listId, string shareToken, Guid itemId)
    {
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, Guid.NewGuid(), "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego Set", "The big one", "https://example.test/lego", DateTimeOffset.UtcNow));
    }

    private Task WaitForItemAsync(string shareToken, Guid itemId) =>
        Eventually.Async(
            async () =>
            {
                var response = await GraphQlClient.QueryAsync(
                    gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken });
                return response.Data?.GetProperty("sharedGiftList").GetProperty("items").EnumerateArray()
                    .Any(i => i.GetProperty("itemId").GetGuid() == itemId) ?? false;
            },
            present => present,
            WaitTimeout);
}
