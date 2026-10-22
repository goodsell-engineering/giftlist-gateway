using System.Text.Json;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gateway.IntegrationTests.GiftLists;

/// <summary>
/// GL-147: an item's priority, projected from <see cref="GiftItemAddedV1"/> and served as
/// <c>priority: Int!</c> on both the owner's <c>giftList(id)</c> and the guest's
/// <c>sharedGiftList(token)</c>. Every projection write rebuilds the embedded item document field
/// by field, so a site that forgets <c>Priority</c> silently resets the item to 3; the tombstone,
/// description-change and stub-fill cases below each pin one of those sites. The change event
/// (<c>GiftItemPriorityChangedV1</c>) is deliberately not subscribed to here: that is GL-149's, and
/// its cases belong in this class.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class GiftItemPriorityProjectionTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GiftListAndSharedGiftList_ShouldServeTheItemsPriority_WhenGiftItemAddedV1CarriesOne()
    {
        // Arrange — 5 is neither the default (3) nor the contract's null
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();

        // Act
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, DateTimeOffset.UtcNow, 5));
        var owner = await WaitForOwnerItemAsync(list, itemId);
        var guest = await WaitForGuestItemAsync(list, itemId);

        // Assert
        Assert.Equal(5, owner.GetProperty("priority").GetInt32());
        Assert.Equal(5, guest.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task GiftListAndSharedGiftList_ShouldServePriority3_WhenGiftItemAddedV1HasNoPriority()
    {
        // Arrange
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();

        // Act — an event from before contracts 0.4.0
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, DateTimeOffset.UtcNow, null));
        var owner = await WaitForOwnerItemAsync(list, itemId);
        var guest = await WaitForGuestItemAsync(list, itemId);

        // Assert
        Assert.Equal(3, owner.GetProperty("priority").GetInt32());
        Assert.Equal(3, guest.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task GiftListAndSharedGiftList_ShouldServePriority3_WhenTheStoredItemHasNoPriorityField()
    {
        // Arrange — a raw BsonDocument, so the field is genuinely absent rather than null
        var list = new ListHandle(Guid.NewGuid(), Guid.NewGuid(), ShareTokens.New());
        var itemId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var document = new BsonDocument
        {
            ["_id"] = new BsonBinaryData(list.ListId, GuidRepresentation.Standard),
            ["ownerId"] = new BsonBinaryData(list.OwnerId, GuidRepresentation.Standard),
            ["name"] = "Birthday Wishlist",
            ["nameUpdatedAt"] = now,
            ["expiresAt"] = now.AddDays(7),
            ["shareToken"] = list.ShareToken,
            ["createdAt"] = now,
            ["hasCreated"] = true,
            ["isDeleted"] = false,
            ["deletedAt"] = BsonNull.Value,
            ["version"] = 0L,
            ["items"] = new BsonArray
            {
                new BsonDocument
                {
                    ["itemId"] = new BsonBinaryData(itemId, GuidRepresentation.Standard),
                    ["name"] = "Lego Set",
                    ["description"] = BsonNull.Value,
                    ["url"] = BsonNull.Value,
                    ["isRemoved"] = false,
                    ["updatedAt"] = now,
                    // deliberately no "priority": a document written before GL-147
                },
            },
        };
        await Items().InsertOneAsync(document);

        // Act
        var owner = await WaitForOwnerItemAsync(list, itemId);
        var guest = await WaitForGuestItemAsync(list, itemId);

        // Assert
        Assert.Equal(3, owner.GetProperty("priority").GetInt32());
        Assert.Equal(3, guest.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task GiftItemRemovedV1_ShouldCarryThePriorityOntoTheTombstone_AndKeepTheItemHidden()
    {
        // Arrange
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedAt = DateTimeOffset.UtcNow;
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, addedAt, 5));
        await WaitForOwnerItemAsync(list, itemId);

        // Act
        await gateway.GiftListsBus.Publish(new GiftItemRemovedV1(list.ListId, itemId, addedAt.AddSeconds(5)));
        var tombstone = await WaitForRawItemAsync(list.ListId, itemId, item => item["isRemoved"].AsBoolean);

        // Assert
        var owner = await QueryOwnerAsync(list);
        Assert.Empty(owner!.Value.GetProperty("items").EnumerateArray());
        Assert.Equal(5, tombstone["priority"].AsInt32);
    }

    [Fact]
    public async Task GiftItemAddedV1_ShouldStayHiddenAndLeaveTheTombstoneUnchanged_WhenItArrivesAfterANewerRemove()
    {
        // Arrange — the remove lands first, on an item the projection has never seen
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedAt = DateTimeOffset.UtcNow;
        await gateway.GiftListsBus.Publish(new GiftItemRemovedV1(list.ListId, itemId, addedAt.AddSeconds(5)));
        await WaitForRawItemAsync(list.ListId, itemId, item => item["isRemoved"].AsBoolean);

        // Act — the older add is consumed (probe count) and ignored
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemAddedV1>();
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, addedAt, 5));
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemAddedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — the stale add wrote nothing, so the tombstone's priority stays null
        var owner = await QueryOwnerAsync(list);
        Assert.Empty(owner!.Value.GetProperty("items").EnumerateArray());
        var tombstone = await WaitForRawItemAsync(list.ListId, itemId, item => item["isRemoved"].AsBoolean);
        Assert.True(tombstone["priority"].IsBsonNull); // the driver writes the null explicitly; the add's 5 must not be there
    }

    [Fact]
    public async Task GiftItemDescriptionChangedV1_ShouldKeepTheItemsPriority()
    {
        // Arrange
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedAt = DateTimeOffset.UtcNow;
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", "Old text", null, addedAt, 5));
        await WaitForOwnerItemAsync(list, itemId);

        // Act
        await gateway.GiftListsBus.Publish(new GiftItemDescriptionChangedV1(list.ListId, itemId, "New text", addedAt.AddSeconds(1)));
        var owner = await WaitForOwnerItemAsync(list, itemId, item => item.GetProperty("description").GetString() == "New text");
        var guest = await WaitForGuestItemAsync(list, itemId, item => item.GetProperty("description").GetString() == "New text");

        // Assert — 5, not the default 3 a forgotten copy would give
        Assert.Equal(5, owner.GetProperty("priority").GetInt32());
        Assert.Equal(5, guest.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task GiftItemAddedV1_ShouldTakeTheAddsPriority_WhenItFillsADescriptionStub()
    {
        // Arrange — the change arrives first and leaves an invisible stub
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var changedAt = DateTimeOffset.UtcNow;
        await gateway.GiftListsBus.Publish(new GiftItemDescriptionChangedV1(list.ListId, itemId, "Early", changedAt));
        await WaitForRawItemAsync(list.ListId, itemId, item => item["isStub"].AsBoolean);
        var hidden = await QueryOwnerAsync(list);
        Assert.Empty(hidden!.Value.GetProperty("items").EnumerateArray());

        // Act — the add, older than the change, fills the stub
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", "Original", null, changedAt.AddSeconds(-5), 4));
        var owner = await WaitForOwnerItemAsync(list, itemId);

        // Assert
        Assert.Equal("Early", owner.GetProperty("description").GetString());
        Assert.Equal(4, owner.GetProperty("priority").GetInt32());
        var raw = await WaitForRawItemAsync(list.ListId, itemId, item => !item["isStub"].AsBoolean);
        Assert.Equal(4, raw["priority"].AsInt32);
    }

    [Fact]
    public async Task GiftItemAddedV1_Redelivery_ShouldLeaveTheProjectionDocumentAndVersionUnchanged_WhenItCarriesAPriority()
    {
        // Arrange
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedEvent = new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, DateTimeOffset.UtcNow, 4);
        await gateway.GiftListsBus.Publish(addedEvent);
        await WaitForOwnerItemAsync(list, itemId);
        var before = await ReadDocumentAsync(list.ListId);

        // Act — redelivered; the probe count proves it was consumed
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemAddedV1>();
        await gateway.GiftListsBus.Publish(addedEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemAddedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — the exact-redelivery check compares priority too, so nothing is rewritten
        var after = await ReadDocumentAsync(list.ListId);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// Same instant, name, description and url, different priority: not a redelivery, so the add's
    /// priority applies. This is the case that fails if the exact-redelivery check ignores priority.
    /// </summary>
    [Fact]
    public async Task GiftItemAddedV1_ShouldTakeTheNewPriority_WhenOnlyThePriorityDiffersAtTheSameInstant()
    {
        // Arrange
        var list = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedAt = DateTimeOffset.UtcNow;
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, addedAt, 4));
        await WaitForOwnerItemAsync(list, itemId);

        // Act
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(list.ListId, itemId, "Lego Set", null, null, addedAt, 2));
        var owner = await WaitForOwnerItemAsync(list, itemId, item => item.GetProperty("priority").GetInt32() == 2);

        // Assert
        Assert.Equal(2, owner.GetProperty("priority").GetInt32());
    }

    private sealed record ListHandle(Guid ListId, Guid OwnerId, string ShareToken);

    private async Task<ListHandle> CreateListAsync()
    {
        var list = new ListHandle(Guid.NewGuid(), Guid.NewGuid(), ShareTokens.New());
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            list.ListId, list.OwnerId, "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), list.ShareToken, DateTimeOffset.UtcNow));
        await Eventually.Async(() => QueryOwnerAsync(list), response => response is not null, WaitTimeout);
        return list;
    }

    private async Task<JsonElement?> QueryOwnerAsync(ListHandle list)
    {
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient,
            GiftListGraphQlQueries.GiftList,
            new { id = list.ListId },
            TestTokenIssuer.IssueAccessToken(list.OwnerId));

        if (response.Data is not { } data)
        {
            return null;
        }

        var giftList = data.GetProperty("giftList");
        return giftList.ValueKind == JsonValueKind.Null ? null : giftList;
    }

    private async Task<JsonElement?> QueryGuestAsync(ListHandle list)
    {
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = list.ShareToken });

        if (response.Data is not { } data)
        {
            return null;
        }

        var shared = data.GetProperty("sharedGiftList");
        return shared.ValueKind == JsonValueKind.Null ? null : shared;
    }

    private Task<JsonElement> WaitForOwnerItemAsync(ListHandle list, Guid itemId, Func<JsonElement, bool>? condition = null) =>
        WaitForItemAsync(() => QueryOwnerAsync(list), itemId, condition);

    private Task<JsonElement> WaitForGuestItemAsync(ListHandle list, Guid itemId, Func<JsonElement, bool>? condition = null) =>
        WaitForItemAsync(() => QueryGuestAsync(list), itemId, condition);

    private static async Task<JsonElement> WaitForItemAsync(
        Func<Task<JsonElement?>> query, Guid itemId, Func<JsonElement, bool>? condition)
    {
        JsonElement? Find(JsonElement? giftList) =>
            giftList?.GetProperty("items").EnumerateArray()
                .Where(i => i.GetProperty("itemId").GetGuid() == itemId)
                .Select(i => (JsonElement?)i)
                .FirstOrDefault();

        var found = await Eventually.Async(
            async () => Find(await query()),
            item => item is { } present && (condition is null || condition(present)),
            WaitTimeout);
        return found!.Value;
    }

    private IMongoCollection<BsonDocument> Items() =>
        gateway.Database.GetCollection<BsonDocument>(GatewayFixture.GiftListProjectionsCollectionName);

    private Task<BsonDocument> ReadDocumentAsync(Guid listId) =>
        Items().Find(Builders<BsonDocument>.Filter.Eq("_id", listId)).FirstAsync();

    /// <summary>The raw embedded item document, waited for until <paramref name="condition"/> holds. Needed because a tombstone or stub is, by design, never visible through a query.</summary>
    private async Task<BsonDocument> WaitForRawItemAsync(Guid listId, Guid itemId, Func<BsonDocument, bool> condition)
    {
        BsonDocument? Find(BsonDocument? document) =>
            document is null || !document.Contains("items")
                ? null
                : document["items"].AsBsonArray
                    .Select(i => i.AsBsonDocument)
                    .FirstOrDefault(i => i["itemId"].AsGuid == itemId);

        var item = await Eventually.Async(
            async () => Find(await Items().Find(Builders<BsonDocument>.Filter.Eq("_id", listId)).FirstOrDefaultAsync()),
            found => found is not null && condition(found),
            WaitTimeout);
        return item!;
    }
}
