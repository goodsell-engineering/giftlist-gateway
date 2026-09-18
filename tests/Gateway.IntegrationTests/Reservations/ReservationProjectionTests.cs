using System.Text.Json;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using MongoDB.Bson;
using MongoDB.Driver;
using Reservations.Contracts.Reservations.Events;

namespace Gateway.IntegrationTests.Reservations;

/// <summary>
/// GL-38: the reservation projection, built off Reservations' real <c>GiftReservedV1</c> on the
/// real broker (<see cref="UpstreamEventPublisher"/> stands in for Reservations, CONVENTIONS.md
/// "Testing") into its own collection, and surfaced — as <c>reserved: boolean</c> and nothing
/// else — through the real <c>/graphql</c> endpoint's <c>sharedGiftList(token)</c>.
///
/// What a guest and an owner may each see of it is <c>ReservationVisibilityTests</c>' subject;
/// this file is about the projection itself — that it is built, what it stores, and that
/// redelivery and reordering leave it correct (CONVENTIONS.md "Messaging").
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class ReservationProjectionTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SharedGiftList_ShouldShowTheItemAsReserved_AfterGiftReservedV1()
    {
        // Arrange — two items, so "reserved" is a fact about one item and not about the list
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        var reservedItemId = Guid.NewGuid();
        var freeItemId = Guid.NewGuid();
        await PublishListWithItemsAsync(listId, shareToken, reservedItemId, freeItemId);
        await WaitForSharedGiftListAsync(shareToken, list => list.GetProperty("items").GetArrayLength() == 2);

        // Act
        await gateway.ReservationsBus.Publish(new GiftReservedV1(listId, reservedItemId, DateTimeOffset.UtcNow));
        var sharedList = await WaitForSharedGiftListAsync(
            shareToken, list => list.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("reserved").GetBoolean()));

        // Assert
        var items = sharedList.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("itemId").GetGuid());
        Assert.True(items[reservedItemId].GetProperty("reserved").GetBoolean());
        Assert.False(items[freeItemId].GetProperty("reserved").GetBoolean());
    }

    /// <summary>
    /// The stored document is the (list, item) pair and nothing else — in particular no
    /// <c>reservedAt</c>, though the event carries one. The guarantee is the absence of data
    /// (ARCHITECTURE.md "Nobody can see *who* reserved"), and a field that is never stored cannot
    /// be surfaced by a later query nobody thought to test. Read straight from the collection the
    /// running Gateway writes to, since no public surface could ever show what the document holds.
    /// </summary>
    [Fact]
    public async Task ReservationProjection_ShouldStoreTheListAndItemIdsAndNothingElse_WhenGiftReservedV1IsProjected()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        // Act
        await gateway.ReservationsBus.Publish(new GiftReservedV1(listId, itemId, DateTimeOffset.UtcNow));
        var document = await WaitForReservationDocumentAsync(listId, itemId);

        // Assert
        Assert.Equal(["_id", "itemId", "listId"], document.Names.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(listId, document["listId"].AsGuid);
        Assert.Equal(itemId, document["itemId"].AsGuid);
    }

    [Fact]
    public async Task GiftReservedV1_Redelivery_ShouldLeaveTheProjectionUnchanged()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var reservedEvent = new GiftReservedV1(listId, itemId, DateTimeOffset.UtcNow);
        await gateway.ReservationsBus.Publish(reservedEvent);
        await WaitForReservationDocumentAsync(listId, itemId);

        // Act — the exact same event, redelivered (CONVENTIONS.md "Messaging": at-least-once). A
        // redelivery upserts onto the same _id and changes nothing, so the collection cannot tell
        // "processed and correctly ignored" apart from "never delivered" — the probe's count can
        // (see GatewayFixture.EventProbe's doc comment).
        var probeBaseline = gateway.EventProbe.CountFor<GiftReservedV1>();
        await gateway.ReservationsBus.Publish(reservedEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftReservedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert
        var count = await ReservationProjections()
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("listId", listId));
        Assert.Equal(1, count);
    }

    /// <summary>
    /// A reservation for a list the Gateway has not yet seen a <c>GiftListCreatedV1</c> for —
    /// delivery across two upstream services is unordered — is kept, not dropped, and shows up
    /// as <c>reserved</c> once the list itself lands. The two projections are separate
    /// collections (ARCHITECTURE.md "Data model"), so neither needs the other to exist to be
    /// written.
    /// </summary>
    [Fact]
    public async Task SharedGiftList_ShouldShowTheItemAsReserved_WhenGiftReservedV1ArrivedBeforeTheListDid()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        var itemId = Guid.NewGuid();
        await gateway.ReservationsBus.Publish(new GiftReservedV1(listId, itemId, DateTimeOffset.UtcNow));
        await WaitForReservationDocumentAsync(listId, itemId);

        // Act
        await PublishListWithItemsAsync(listId, shareToken, itemId, Guid.NewGuid());
        var sharedList = await WaitForSharedGiftListAsync(
            shareToken, list => list.GetProperty("items").GetArrayLength() == 2);

        // Assert
        var item = Assert.Single(sharedList.GetProperty("items").EnumerateArray(), i => i.GetProperty("itemId").GetGuid() == itemId);
        Assert.True(item.GetProperty("reserved").GetBoolean());
    }

    private async Task PublishListWithItemsAsync(Guid listId, string shareToken, Guid firstItemId, Guid secondItemId)
    {
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, Guid.NewGuid(), "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, firstItemId, "Lego Set", "The big one", "https://example.test/lego", DateTimeOffset.UtcNow));
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, secondItemId, "Coffee grinder", null, null, DateTimeOffset.UtcNow));
    }

    private IMongoCollection<BsonDocument> ReservationProjections() =>
        gateway.Database.GetCollection<BsonDocument>(GatewayFixture.ReservationProjectionsCollectionName);

    private Task<BsonDocument> WaitForReservationDocumentAsync(Guid listId, Guid itemId) =>
        Eventually.Async(
            async () => await ReservationProjections()
                .Find(Builders<BsonDocument>.Filter.Eq("listId", listId) & Builders<BsonDocument>.Filter.Eq("itemId", itemId))
                .FirstOrDefaultAsync(),
            document => document is not null,
            WaitTimeout);

    private async Task<JsonElement> WaitForSharedGiftListAsync(string shareToken, Func<JsonElement, bool> condition) =>
        await Eventually.Async(
            async () =>
            {
                var response = await GraphQlClient.QueryAsync(
                    gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken });
                if (response.Data is not { } data)
                {
                    return (JsonElement?)null;
                }

                var list = data.GetProperty("sharedGiftList");
                return list.ValueKind == JsonValueKind.Null ? null : list;
            },
            list => list is { } sharedList && condition(sharedList),
            WaitTimeout) ?? throw new InvalidOperationException("unreachable");
}
