using System.Text.Json;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gateway.IntegrationTests.GiftLists;

/// <summary>
/// GL-23: the Gateway's own read model, built entirely off GiftLists' integration events on the
/// real broker (a <see cref="GiftListsEventPublisher"/> stands in for GiftLists, CONVENTIONS.md
/// "Testing"), and served through the real <c>/graphql</c> endpoint (<see cref="GraphQlClient"/>).
///
/// Covers, at minimum (GL-23 review, Batch 12):
/// <list type="bullet">
/// <item>redelivery of each event type leaves the projection unchanged;</item>
/// <item>an add reordered after its own remove (the redelivery-after-a-lost-GL-64-race hazard) does not resurrect the item;</item>
/// <item>a non-owner cannot read another owner's list through either query.</item>
/// </list>
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class GiftListProjectionTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MyGiftLists_ShouldReturnTheList_WhenTheOwnerQueriesAfterCreation()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Birthday Wishlist", expiresAt, "share-token-1", createdAt));
        var response = await WaitForGiftListAsync(listId, ownerId);

        // Assert
        Assert.Equal(listId, GetGuid(response, "listId"));
        Assert.Equal(ownerId, GetGuid(response, "ownerId"));
        Assert.Equal("Birthday Wishlist", response.GetProperty("name").GetString());
        Assert.Equal("share-token-1", response.GetProperty("shareToken").GetString());
        Assert.Empty(response.GetProperty("items").EnumerateArray());

        var myLists = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.MyGiftLists, accessToken: TestTokenIssuer.IssueAccessToken(ownerId));
        Assert.Contains(myLists.Data!.Value.GetProperty("myGiftLists").EnumerateArray(), l => GetGuid(l, "listId") == listId);
    }

    [Fact]
    public async Task GiftList_ShouldIncludeTheItem_AfterGiftItemAddedV1()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var itemId = Guid.NewGuid();

        // Act
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego Set", "The big one", "https://example.test/lego", DateTimeOffset.UtcNow));
        var response = await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("items").GetArrayLength() > 0);

        // Assert
        var item = Assert.Single(response.GetProperty("items").EnumerateArray());
        Assert.Equal(itemId, GetGuid(item, "itemId"));
        Assert.Equal("Lego Set", item.GetProperty("name").GetString());
        Assert.Equal("The big one", item.GetProperty("description").GetString());
        Assert.Equal("https://example.test/lego", item.GetProperty("url").GetString());
    }

    [Fact]
    public async Task GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var createdEvent = new GiftListCreatedV1(
            listId, ownerId, "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), "share-token-2", DateTimeOffset.UtcNow);
        await gateway.GiftListsBus.Publish(createdEvent);
        await WaitForGiftListAsync(listId, ownerId);

        // Act — the exact same event, redelivered (CONVENTIONS.md "Messaging": at-least-once delivery).
        // A byte-identical redelivery causes no write at all (GiftListProjectionRepository's
        // read-mutate-write loop only writes when something would change), so the projection
        // itself cannot tell "processed and correctly ignored" apart from "never delivered" — the
        // probe's own count can (see GatewayFixture.EventProbe's doc comment).
        var probeBaseline = gateway.EventProbe.CountFor<GiftListCreatedV1>();
        await gateway.GiftListsBus.Publish(createdEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftListCreatedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert
        var response = await QueryGiftListAsync(listId, ownerId);
        Assert.Equal("Birthday Wishlist", response!.Value.GetProperty("name").GetString());
        Assert.Equal("share-token-2", response.Value.GetProperty("shareToken").GetString());
        Assert.Empty(response.Value.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GiftListRenamedV1_Redelivery_ShouldLeaveTheProjectionUnchanged()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var renamedEvent = new GiftListRenamedV1(listId, "New Name", DateTimeOffset.UtcNow);
        await gateway.GiftListsBus.Publish(renamedEvent);
        await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("name").GetString() == "New Name");

        // Act — redelivered; see GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged
        // for why the probe count, not the projection, is what proves this was consumed.
        var probeBaseline = gateway.EventProbe.CountFor<GiftListRenamedV1>();
        await gateway.GiftListsBus.Publish(renamedEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftListRenamedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert
        var response = await QueryGiftListAsync(listId, ownerId);
        Assert.Equal("New Name", response!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GiftListDeletedV1_Redelivery_ShouldLeaveTheProjectionDeleted()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var deletedEvent = new GiftListDeletedV1(listId, DateTimeOffset.UtcNow);
        await gateway.GiftListsBus.Publish(deletedEvent);
        await Eventually.Async(
            () => QueryGiftListAsync(listId, ownerId),
            response => response is null,
            WaitTimeout);

        // Act — redelivered; see GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged
        // for why the probe count, not the projection, is what proves this was consumed.
        var probeBaseline = gateway.EventProbe.CountFor<GiftListDeletedV1>();
        await gateway.GiftListsBus.Publish(deletedEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftListDeletedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — still gone, not resurrected
        var response = await QueryGiftListAsync(listId, ownerId);
        Assert.Null(response);
    }

    [Fact]
    public async Task GiftItemAddedV1_Redelivery_ShouldLeaveTheProjectionUnchanged()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedEvent = new GiftItemAddedV1(listId, itemId, "Lego Set", null, null, DateTimeOffset.UtcNow);
        await gateway.GiftListsBus.Publish(addedEvent);
        await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("items").GetArrayLength() > 0);

        // Act — redelivered; see GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged
        // for why the probe count, not the projection, is what proves this was consumed.
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemAddedV1>();
        await gateway.GiftListsBus.Publish(addedEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemAddedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — exactly one item, never a duplicate append
        var response = await QueryGiftListAsync(listId, ownerId);
        var item = Assert.Single(response!.Value.GetProperty("items").EnumerateArray());
        Assert.Equal(itemId, GetGuid(item, "itemId"));
    }

    [Fact]
    public async Task GiftItemRemovedV1_Redelivery_ShouldLeaveTheProjectionUnchanged()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var itemId = Guid.NewGuid();
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(listId, itemId, "Lego Set", null, null, DateTimeOffset.UtcNow));
        await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("items").GetArrayLength() > 0);

        var removedEvent = new GiftItemRemovedV1(listId, itemId, DateTimeOffset.UtcNow);
        await gateway.GiftListsBus.Publish(removedEvent);
        await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("items").GetArrayLength() == 0);

        // Act — redelivered; see GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged
        // for why the probe count, not the projection, is what proves this was consumed.
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemRemovedV1>();
        await gateway.GiftListsBus.Publish(removedEvent);
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemRemovedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert
        var response = await QueryGiftListAsync(listId, ownerId);
        Assert.Empty(response!.Value.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// The hazard GL-23's review comments call out by name: GL-64 can redeliver
    /// <see cref="GiftItemAddedV1"/> seconds after the message that actually won a race, so its
    /// own <c>AddedAt</c> can reach this projection <em>after</em> a <see cref="GiftItemRemovedV1"/>
    /// for the same item whose own <c>RemovedAt</c> is chronologically later. Idempotency alone
    /// does not catch this — only comparing timestamps does (<c>GiftListProjectionRepository</c>'s
    /// own doc comment has the mechanism).
    /// </summary>
    [Fact]
    public async Task GiftItemAddedV1_ShouldNotResurrectTheItem_WhenItArrivesAfterAReorderedGiftItemRemovedV1()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var addedAt = DateTimeOffset.UtcNow;
        var removedAt = addedAt.AddSeconds(5); // chronologically later than the add below

        // Act — the remove is processed first (its message simply arrives first); the add is
        // processed only once the remove's tombstone is confirmed written, so this test does not
        // depend on Rebus's own delivery ordering to reproduce the reordering GL-64 causes.
        await gateway.GiftListsBus.Publish(new GiftItemRemovedV1(listId, itemId, removedAt));
        await WaitForRawItemStateAsync(listId, itemId, isRemoved: true);

        // The add is genuinely stale (older than the already-applied remove) so it too causes no
        // write — see GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged for why the
        // probe count, not the projection, is what proves it was consumed rather than never sent.
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemAddedV1>();
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(listId, itemId, "Lego Set", null, null, addedAt));
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemAddedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — the add lost: no item, resurrected or otherwise
        var response = await QueryGiftListAsync(listId, ownerId);
        Assert.Empty(response!.Value.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// GL-31: the read-model half of the share link. No GraphQL surface calls
    /// <c>FindByShareTokenAsync</c> yet (GL-32 owns that), so this reaches the port directly
    /// through the composition root (<see cref="GatewayFixture.GiftListProjections"/>,
    /// CONVENTIONS.md "Reaching an internal from a test" route 2) rather than waiting for a query
    /// that does not exist.
    /// </summary>
    [Fact]
    public async Task FindByShareTokenAsync_ShouldReturnTheList_WhenTheTokenMatches()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var shareToken = $"share-{Guid.NewGuid():N}";
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await WaitForGiftListAsync(listId, ownerId);

        // Act
        var found = await Eventually.Async(
            () => gateway.GiftListProjections.FindByShareTokenAsync(shareToken, CancellationToken.None),
            projection => projection is not null,
            WaitTimeout);

        // Assert
        Assert.Equal(listId, found!.ListId);
        Assert.Equal(ownerId, found.OwnerId);
        Assert.Equal(shareToken, found.ShareToken);
    }

    [Fact]
    public async Task FindByShareTokenAsync_ShouldReturnNull_WhenNoListCarriesTheToken()
    {
        // Arrange
        var unknownToken = $"share-{Guid.NewGuid():N}";

        // Act
        var found = await gateway.GiftListProjections.FindByShareTokenAsync(unknownToken, CancellationToken.None);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task GiftList_ShouldReturnForbidden_WhenANonOwnerRequestsIt()
    {
        // Arrange
        var (listId, _) = await CreateListAsync();
        var nonOwnerId = Guid.NewGuid();

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient,
            GiftListGraphQlQueries.GiftList,
            new { id = listId },
            TestTokenIssuer.IssueAccessToken(nonOwnerId));

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.forbidden", error.ErrorCode);
        Assert.Equal("FORBIDDEN", error.Code);
    }

    [Fact]
    public async Task MyGiftLists_ShouldNotIncludeAnotherOwnersList_WhenQueriedByANonOwner()
    {
        // Arrange
        var (listId, ownerId) = await CreateListAsync();
        var nonOwnerId = Guid.NewGuid();
        Assert.NotEqual(ownerId, nonOwnerId);

        // Act — a query that only ever runs as the owner cannot fail for the reason this test
        // exists (GL-23 review), so this deliberately authenticates as a second, different user.
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient,
            GiftListGraphQlQueries.MyGiftLists,
            accessToken: TestTokenIssuer.IssueAccessToken(nonOwnerId));

        // Assert
        Assert.DoesNotContain(response.Data!.Value.GetProperty("myGiftLists").EnumerateArray(), l => GetGuid(l, "listId") == listId);
    }

    [Fact]
    public async Task GiftList_ShouldReturnUnauthenticated_WhenNoAccessTokenIsSent()
    {
        // Arrange
        var (listId, _) = await CreateListAsync();

        // Act
        var response = await GraphQlClient.QueryAsync(gateway.GraphQlHttpClient, GiftListGraphQlQueries.GiftList, new { id = listId });

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.unauthenticated", error.ErrorCode);
        Assert.Equal("UNAUTHENTICATED", error.Code);
    }

    [Fact]
    public async Task GiftItemAddedV1_ShouldNotResurrectTheItem_WhenItSharesTheRemovesExactTimestamp()
    {
        // Arrange — the tie case, not the strictly-reordered one. The existing resurrection test
        // uses a remove that is chronologically LATER than the add; this one gives both events the
        // SAME instant. That distinction stopped being academic in this batch: GL-66 normalised
        // these timestamps to millisecond resolution, so an exact collision went from vanishingly
        // unlikely at 100ns ticks to an ordinary occurrence (Batch 13 review demonstrated the
        // resurrection).
        var (listId, ownerId) = await CreateListAsync();
        var itemId = Guid.NewGuid();
        var sharedInstant = DateTimeOffset.UtcNow;

        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego", null, null, sharedInstant.AddSeconds(-1)));
        await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("items").GetArrayLength() > 0);
        await gateway.GiftListsBus.Publish(new GiftItemRemovedV1(listId, itemId, sharedInstant));
        await WaitForRawItemStateAsync(listId, itemId, isRemoved: true);

        // Act — the add arrives again carrying the remove's exact timestamp. The tie resolves in
        // the tombstone's favour (UpsertOnAdded), so this too causes no write — see
        // GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged for why the probe count,
        // not the projection, is what proves it was consumed rather than never sent.
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemAddedV1>();
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego", null, null, sharedInstant));
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemAddedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — the tombstone wins the tie, so the two orderings converge
        var response = await WaitForGiftListAsync(listId, ownerId);
        Assert.Empty(response.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GiftListCreatedV1_ShouldNotRevertTheName_WhenItSharesTheRenamesExactTimestamp()
    {
        // Arrange — the same tie question for the list-level guards. MutateOnRenamed lets a rename
        // apply on a tie, so MutateOnCreated had to agree; while it used >= the projection's
        // settled name depended on which of the two arrived last (Batch 13 review).
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var sharedInstant = DateTimeOffset.UtcNow;
        var shareToken = $"share-{Guid.NewGuid():N}";

        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Original", DateTimeOffset.UtcNow.AddDays(7), shareToken, sharedInstant.AddSeconds(-1)));
        await WaitForGiftListAsync(listId, ownerId);
        await gateway.GiftListsBus.Publish(new GiftListRenamedV1(listId, "Renamed", sharedInstant));
        await WaitForGiftListAsync(listId, ownerId, r => r.GetProperty("name").GetString() == "Renamed");

        // Act — a redelivered Created carrying the rename's exact timestamp. MutateOnCreated's
        // applyName guard means the name field itself does not get rewritten — see
        // GiftListCreatedV1_Redelivery_ShouldLeaveTheProjectionUnchanged for why the probe count,
        // not the projection, is what proves this was consumed rather than never sent.
        var probeBaseline = gateway.EventProbe.CountFor<GiftListCreatedV1>();
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Original", DateTimeOffset.UtcNow.AddDays(7), shareToken, sharedInstant));
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftListCreatedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert
        var response = await WaitForGiftListAsync(listId, ownerId);
        Assert.Equal("Renamed", response.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GiftList_ShouldStayInvisibleThenMaterialise_WhenAnItemEventArrivesBeforeItsCreated()
    {
        // Arrange — the out-of-order-before-Created path. Every other test in this file calls
        // CreateListAsync() first, so the branch that handles a non-Created event for a list that
        // does not exist yet (the stub, HasCreated, and the applyName guard around it) had NO test
        // at all — roughly sixty lines written unprompted, and the code the tie guards above sit
        // inside (Batch 13 review). It works; it just was not covered.
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var itemAddedAt = DateTimeOffset.UtcNow;

        // Act — an item event for a list the Gateway has never heard of. Unlike the eight
        // redelivery/tie tests above, this one already had a real positive signal without the
        // probe: it is a brand-new list, so a stub row can only exist below if this event was
        // actually processed (MutateOnItemAdded always inserts for a never-seen item) — but the
        // probe wait replaces the fixed delay here too, for the same reason and the same speed.
        var probeBaseline = gateway.EventProbe.CountFor<GiftItemAddedV1>();
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Coffee grinder", "Burr, not blade", "https://example.com", itemAddedAt));
        await Eventually.Async(
            () => Task.FromResult(gateway.EventProbe.CountFor<GiftItemAddedV1>()),
            count => count > probeBaseline,
            WaitTimeout);

        // Assert — a stub is not a list: it must not be readable, or an unowned row would surface.
        Assert.Null(await QueryGiftListAsync(listId, ownerId));

        // ...asserted against the raw document as well, because the GraphQL check alone cannot
        // fail for the right reason: a stub carries OwnerId = Guid.Empty, so if HasCreated were
        // wrongly true the ownership check would reject the query anyway and the assertion above
        // would still see null. Mutation-tested — flipping the stub's HasCreated to true leaves
        // the GraphQL assertion green (Batch 13). This one pins the stub semantics directly.
        var stub = await gateway.Database
            .GetCollection<BsonDocument>(GatewayFixture.GiftListProjectionsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", listId))
            .FirstOrDefaultAsync();
        Assert.NotNull(stub);
        Assert.False(stub["hasCreated"].AsBoolean);

        // Act — its Created finally arrives
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7),
            $"share-{Guid.NewGuid():N}", itemAddedAt.AddSeconds(1)));

        // Assert — the list materialises AND the earlier item survived rather than being dropped
        var response = await WaitForGiftListAsync(listId, ownerId);
        Assert.Equal("Birthday Wishlist", response.GetProperty("name").GetString());
        var item = Assert.Single(response.GetProperty("items").EnumerateArray());
        Assert.Equal("Coffee grinder", item.GetProperty("name").GetString());
    }

    private async Task<(Guid ListId, Guid OwnerId)> CreateListAsync()
    {
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), $"share-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        await WaitForGiftListAsync(listId, ownerId);
        return (listId, ownerId);
    }

    private async Task<JsonElement> WaitForGiftListAsync(Guid listId, Guid ownerId, Func<JsonElement, bool>? extraCondition = null) =>
        await Eventually.Async(
            () => QueryGiftListAsync(listId, ownerId),
            response => response is { } giftList && (extraCondition is null || extraCondition(giftList)),
            WaitTimeout) ?? throw new InvalidOperationException("unreachable");

    private async Task<JsonElement?> QueryGiftListAsync(Guid listId, Guid ownerId)
    {
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient,
            GiftListGraphQlQueries.GiftList,
            new { id = listId },
            TestTokenIssuer.IssueAccessToken(ownerId));

        if (response.Data is not { } data)
        {
            return null;
        }

        var giftList = data.GetProperty("giftList");
        return giftList.ValueKind == JsonValueKind.Null ? null : giftList;
    }

    /// <summary>
    /// Reads the projection's own persisted state directly (CONVENTIONS.md "Testing" permits asserting
    /// on infrastructure directly, not just through the public surface) — needed here because a
    /// removed-but-never-added item's tombstone is, by design, never observable through
    /// <c>giftList(id)</c> (<c>GiftListProjectionDocumentMapper</c> filters it out); this is the
    /// only way to know the remove actually landed before publishing the reordered add.
    /// </summary>
    private async Task WaitForRawItemStateAsync(Guid listId, Guid itemId, bool isRemoved)
    {
        var collection = gateway.Database.GetCollection<BsonDocument>(GatewayFixture.GiftListProjectionsCollectionName);

        await Eventually.Async(
            async () => await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", listId)).FirstOrDefaultAsync(),
            document =>
            {
                if (document is null || !document.Contains("items"))
                {
                    return false;
                }

                return document["items"].AsBsonArray
                    .Select(i => i.AsBsonDocument)
                    .Any(i => i["itemId"].AsGuid == itemId && i["isRemoved"].AsBoolean == isRemoved);
            },
            WaitTimeout);
    }

    private static Guid GetGuid(JsonElement element, string property) => element.GetProperty(property).GetGuid();
}
