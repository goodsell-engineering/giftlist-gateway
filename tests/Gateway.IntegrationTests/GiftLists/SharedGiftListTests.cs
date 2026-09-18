using System.Text.Json;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gateway.IntegrationTests.GiftLists;

/// <summary>
/// GL-32: the Gateway's one unauthenticated GraphQL surface, <c>sharedGiftList(token)</c>, driven
/// end to end — GiftLists' real integration events on the real broker build the projection
/// (<see cref="UpstreamEventPublisher"/>), and every query below enters through the real
/// <c>/graphql</c> endpoint with <b>no access token at all</b> (CONVENTIONS.md "Testing").
///
/// Separate from <c>GiftListProjectionTests</c>, which is about how the projection is built out of
/// redelivered and reordered events; this file is about what a caller holding only a share link
/// can and cannot see through it.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class SharedGiftListTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SharedGiftList_ShouldReturnTheList_WhenAnAnonymousCallerPresentsItsToken()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        var itemId = Guid.NewGuid();
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, Guid.NewGuid(), "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego Set", "The big one", "https://example.test/lego", DateTimeOffset.UtcNow));

        // Act — no accessToken argument: the token in the variables is the whole credential
        // (ARCHITECTURE.md "Auth & sharing").
        var sharedList = await WaitForSharedGiftListAsync(
            shareToken, list => list.GetProperty("items").GetArrayLength() > 0);

        // Assert
        Assert.Equal(listId, sharedList.GetProperty("listId").GetGuid());
        Assert.Equal("Birthday Wishlist", sharedList.GetProperty("name").GetString());
        var item = Assert.Single(sharedList.GetProperty("items").EnumerateArray());
        Assert.Equal(itemId, item.GetProperty("itemId").GetGuid());
        Assert.Equal("Lego Set", item.GetProperty("name").GetString());
    }

    /// <summary>
    /// THE test this issue exists for. Asserting on the fields that <em>are</em> present would pass
    /// just as happily against a response that also carried <c>ownerId</c> and <c>shareToken</c>,
    /// so this asks for them by name: the anonymous view is its own type with no such fields
    /// (<c>SharedGiftListView</c>), which makes this a schema error rather than a data decision
    /// taken in a resolver — ARCHITECTURE.md "Defence in depth on the owner-facing path": an
    /// <c>if (isOwner) hideField()</c> is one refactor from leaking.
    /// </summary>
    [Fact]
    public async Task SharedGiftList_ShouldHaveNoOwnerIdOrShareTokenField_WhenTheyAreAskedForByName()
    {
        // Arrange
        var shareToken = ShareTokens.New();
        await PublishListAsync(Guid.NewGuid(), shareToken, DateTimeOffset.UtcNow.AddDays(7));
        await WaitForSharedGiftListAsync(shareToken);

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftListOwnerFields, new { token = shareToken });

        // Assert — the query never runs: both fields are rejected by schema validation, and no
        // data comes back at all.
        Assert.Null(response.Data);
        Assert.Contains(response.Errors, e => e.Message.Contains("ownerId", StringComparison.Ordinal));
        Assert.Contains(response.Errors, e => e.Message.Contains("shareToken", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ryan's decision, 2026-09-16, recorded in ARCHITECTURE.md "Auth &amp; sharing": an expired
    /// list stays visible through the share link, read-only — expiry gates reserving, not viewing.
    /// Without this test that rule is a comment, and the "obvious" filter someone adds later goes
    /// in green.
    /// </summary>
    [Fact]
    public async Task SharedGiftList_ShouldStillReturnTheList_WhenItHasAlreadyExpired()
    {
        // Arrange
        var shareToken = ShareTokens.New();
        var expiredAt = DateTimeOffset.UtcNow.AddDays(-3);
        await PublishListAsync(Guid.NewGuid(), shareToken, expiredAt);

        // Act
        var sharedList = await WaitForSharedGiftListAsync(shareToken);

        // Assert — returned, and carrying the past expiry the SPA renders its banner from (GL-42)
        Assert.Equal("Birthday Wishlist", sharedList.GetProperty("name").GetString());
        Assert.True(sharedList.GetProperty("expiresAt").GetDateTimeOffset() < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SharedGiftList_ShouldReturnNotFound_WhenNoListCarriesTheToken()
    {
        // Arrange — well-formed, and belonging to nothing
        var unknownToken = ShareTokens.New();

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = unknownToken });

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.not_found", error.ErrorCode);
        Assert.Equal("NOT_FOUND", error.Code);
    }

    /// <summary>
    /// The boundary shape check (<c>GetSharedGiftListValidator</c>), which is what stops an
    /// unauthenticated caller driving arbitrary-length, arbitrary-content strings at Mongo. Note
    /// the answer differs from <see cref="SharedGiftList_ShouldReturnNotFound_WhenNoListCarriesTheToken"/>
    /// above, deliberately: "that is not a share token" and "no list has that share token" are
    /// different facts, and the token's shape is public anyway — it is visible in every share URL.
    ///
    /// The <c>share-…</c> rows are not invented: they are the shapes this suite itself used to
    /// fabricate before GL-104, which no publisher could ever emit.
    ///
    /// The trailing-newline row is the one that was actually getting through (Batch 34 review).
    /// Note what the gap looked like from inside: the one-short and one-over rows below already
    /// covered "wrong length" as thoroughly as anyone would think to, which is exactly why nobody
    /// suspected the <em>anchor</em> — .NET's <c>$</c> matches before a trailing <c>\n</c>, so a
    /// 22-character token passed a rule that reads as though it cannot. Length coverage says
    /// nothing about where the pattern thinks the string ends.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("share-abc")]
    [InlineData("share-0123456789abcdef0123456789abcdef")] // 38 chars, with a hyphen — the GL-104 shape
    [InlineData("0123456789abcdef0123")] // 20: one short
    [InlineData("0123456789abcdef012345")] // 22: one over
    [InlineData("0123456789abcdef0123!")] // 21, but '!' is not base62
    [InlineData("0123456789abcdef01234\n")] // 22: a legal 21-character token and then a newline — see the summary
    public async Task SharedGiftList_ShouldReturnBadUserInput_WhenTheTokenIsNotTwentyOneBase62Characters(string token)
    {
        // Arrange — nothing: an ill-formed token is rejected before any list could be relevant

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token });

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.invalid_share_token", error.ErrorCode);
        Assert.Equal("BAD_USER_INPUT", error.Code);
    }

    /// <summary>
    /// The token comparison is case-sensitive, and that is load-bearing: <c>ShareToken</c>'s
    /// alphabet is base62, so <c>aB…</c> and <c>Ab…</c> are two different capabilities and one
    /// must not open the other's list. Deterministic on purpose — <see cref="ShareTokens.New"/>
    /// draws at random, so it makes mixed case overwhelmingly likely but pins nothing (Batch 34
    /// review: while that helper produced hex, the suite never presented a token whose case
    /// mattered at all).
    /// </summary>
    [Fact]
    public async Task SharedGiftList_ShouldReturnNotFound_WhenTheTokenDiffersOnlyByCase()
    {
        // Arrange
        const string shareToken = "aBcDeFgHiJkLmNoPqRsTu";
        const string swappedCase = "AbCdEfGhIjKlMnOpQrStU";
        Assert.Equal(shareToken.Length, swappedCase.Length);
        await PublishListAsync(Guid.NewGuid(), shareToken, DateTimeOffset.UtcNow.AddDays(7));
        await WaitForSharedGiftListAsync(shareToken);

        // Act — same letters, every one of them the other case: a well-formed token, and not this one
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = swappedCase });

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.not_found", error.ErrorCode);
        Assert.Equal("NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task SharedGiftList_ShouldReturnNotFound_WhenTheListHasBeenDeleted()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        await PublishListAsync(listId, shareToken, DateTimeOffset.UtcNow.AddDays(7));
        await WaitForSharedGiftListAsync(shareToken);
        await gateway.GiftListsBus.Publish(new GiftListDeletedV1(listId, DateTimeOffset.UtcNow));

        // Act — a soft-deleted row keeps its shareToken in the collection, so "deleted" has to be
        // enforced by the query itself; this is the only thing that proves it is.
        var response = await Eventually.Async(
            () => GraphQlClient.QueryAsync(
                gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken }),
            r => r.Errors.Count > 0,
            WaitTimeout);

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.not_found", error.ErrorCode);
        Assert.Equal("NOT_FOUND", error.Code);
    }

    /// <summary>
    /// A stub row — one created by an item event that outran its own list's <c>GiftListCreatedV1</c>
    /// (CONVENTIONS.md "Messaging": delivery is at-least-once and out of order) — carries
    /// <c>HasCreated == false</c> and an empty <c>shareToken</c>. It must not be reachable through
    /// the share link under any token, including the one its <c>Created</c> event is about to bring.
    /// </summary>
    [Fact]
    public async Task SharedGiftList_ShouldReturnNotFound_WhenTheListIsStillOnlyAStub()
    {
        // Arrange — an item event for a list the Gateway has never heard of, then wait for the
        // stub itself so this cannot pass merely because nothing was processed yet.
        var listId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, Guid.NewGuid(), "Coffee grinder", "Burr, not blade", "https://example.test", DateTimeOffset.UtcNow));
        await WaitForStubAsync(listId);

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken });

        // Assert
        var error = Assert.Single(response.Errors);
        Assert.Equal("gateway.not_found", error.ErrorCode);
        Assert.Equal("NOT_FOUND", error.Code);
    }

    private Task PublishListAsync(Guid listId, string shareToken, DateTimeOffset expiresAt) =>
        gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, Guid.NewGuid(), "Birthday Wishlist", expiresAt, shareToken, DateTimeOffset.UtcNow));

    private async Task<JsonElement> WaitForSharedGiftListAsync(
        string shareToken, Func<JsonElement, bool>? extraCondition = null) =>
        await Eventually.Async(
            () => QuerySharedGiftListAsync(shareToken),
            list => list is { } sharedList && (extraCondition is null || extraCondition(sharedList)),
            WaitTimeout) ?? throw new InvalidOperationException("unreachable");

    private async Task<JsonElement?> QuerySharedGiftListAsync(string shareToken)
    {
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken });

        if (response.Data is not { } data)
        {
            return null;
        }

        var sharedList = data.GetProperty("sharedGiftList");
        return sharedList.ValueKind == JsonValueKind.Null ? null : sharedList;
    }

    /// <summary>
    /// Waits for the raw stub document by reading the projection's Mongo collection. No public
    /// surface could report that a stub has landed — a stub is by design invisible through every
    /// query this Gateway exposes — so the only thing that can say it arrived is the collection it
    /// was written to. That collection is the real database the running Gateway writes to rather
    /// than a stand-in (CONVENTIONS.md "Testing": integration tests run all infrastructure for
    /// real, via Testcontainers), so what this reads is the production write path's own output.
    /// </summary>
    private Task<BsonDocument> WaitForStubAsync(Guid listId) =>
        Eventually.Async(
            async () => await gateway.Database
                .GetCollection<BsonDocument>(GatewayFixture.GiftListProjectionsCollectionName)
                .Find(Builders<BsonDocument>.Filter.Eq("_id", listId))
                .FirstOrDefaultAsync(),
            document => document is not null && !document["hasCreated"].AsBoolean,
            WaitTimeout);
}
