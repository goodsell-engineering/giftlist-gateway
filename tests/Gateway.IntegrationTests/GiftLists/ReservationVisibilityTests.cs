using System.Text.Json;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using GiftLists.Contracts.GiftLists.Events;
using Reservations.Contracts.Reservations.Events;

namespace Gateway.IntegrationTests.GiftLists;

/// <summary>
/// GL-38: what each viewer may see of reservation state, proven against the real schema and the
/// real responses — ARCHITECTURE.md "Nobody can see *who* reserved" (the guest gets
/// <c>reserved: boolean</c> and nothing more) and "Defence in depth on the owner-facing path"
/// (the owner gets nothing at all, omitted rather than hidden).
///
/// Two kinds of proof, deliberately both. Asking for a forbidden field <em>by name</em> proves
/// the schema rejects it — a type-level guarantee no resolver edit can undo. Introspection then
/// proves the field set is <em>exactly</em> what it should be, since a by-name test can only
/// name the fields its author thought of. And one test reads the owner's response before and
/// after a reservation, because a schema says what can be asked for, not what changes.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class ReservationVisibilityTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SharedGiftList_ShouldHaveNoReservedAtOrReserverOrSecretField_WhenTheyAreAskedForByName()
    {
        // Arrange
        var shareToken = ShareTokens.New();
        await PublishListAsync(Guid.NewGuid(), Guid.NewGuid(), shareToken, Guid.NewGuid());

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftListReservationDetailFields, new { token = shareToken });

        // Assert — rejected by schema validation: no data at all, and each name called out
        Assert.Null(response.Data);
        foreach (var field in new[] { "reservedAt", "reservedBy", "reservationId", "releaseSecret" })
        {
            Assert.Contains(response.Errors, e => e.Message.Contains(field, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task GiftList_ShouldHaveNoReservedField_WhenTheOwnerAsksForItByName()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await PublishListAsync(listId, ownerId, ShareTokens.New(), Guid.NewGuid());

        // Act
        var response = await GraphQlClient.QueryAsync(
            gateway.GraphQlHttpClient,
            GiftListGraphQlQueries.GiftListReservedField,
            new { id = listId },
            TestTokenIssuer.IssueAccessToken(ownerId));

        // Assert
        Assert.Null(response.Data);
        Assert.Contains(response.Errors, e => e.Message.Contains("reserved", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole reservation surface of the schema, in one place: the guest's item has exactly
    /// five fields of which <c>reserved</c> is the only reservation fact and is a non-null
    /// <c>Boolean</c>; the owner's item has exactly four and no reservation fact at all; the
    /// subscription type has exactly one field, taking exactly a <c>token</c> — no id, nothing an
    /// owner could subscribe with (ARCHITECTURE.md "Realtime updates": "no owner-facing
    /// subscription carrying reservation data").
    /// </summary>
    [Fact]
    public async Task Schema_ShouldExposeReservedAsTheOnlyReservationFactAndNoOwnerFacingSubscription()
    {
        // Arrange — nothing to publish: this is a question to the schema, not to data

        // Act
        var response = await GraphQlClient.QueryAsync(gateway.GraphQlHttpClient, GiftListGraphQlQueries.ReservationSurfaceIntrospection);

        // Assert
        Assert.Empty(response.Errors);
        var data = response.Data!.Value;

        var guestFields = data.GetProperty("guestItem").GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(
            ["description", "itemId", "name", "reserved", "url"],
            guestFields.Select(f => f.GetProperty("name").GetString()).OrderBy(n => n, StringComparer.Ordinal));
        var reserved = Assert.Single(guestFields, f => f.GetProperty("name").GetString() == "reserved");
        Assert.Equal("NON_NULL", reserved.GetProperty("type").GetProperty("kind").GetString());
        Assert.Equal("Boolean", reserved.GetProperty("type").GetProperty("ofType").GetProperty("name").GetString());

        Assert.Equal(
            ["description", "itemId", "name", "url"],
            data.GetProperty("ownerItem").GetProperty("fields").EnumerateArray()
                .Select(f => f.GetProperty("name").GetString()).OrderBy(n => n, StringComparer.Ordinal));

        var subscriptionField = Assert.Single(
            data.GetProperty("__schema").GetProperty("subscriptionType").GetProperty("fields").EnumerateArray());
        Assert.Equal("sharedGiftListChanged", subscriptionField.GetProperty("name").GetString());
        var argument = Assert.Single(subscriptionField.GetProperty("args").EnumerateArray());
        Assert.Equal("token", argument.GetProperty("name").GetString());
    }

    /// <summary>
    /// The owner's response is byte-for-byte the same before and after an item on their list is
    /// reserved. The schema tests above say the owner cannot <em>ask</em>; this says the owner
    /// path was not even <em>told</em> — the projection it reads does not change, because the
    /// two are separate collections joined only inside <c>ViewGiftList</c>'s guest branch
    /// (ARCHITECTURE.md "Data model").
    /// </summary>
    [Fact]
    public async Task GiftList_ShouldReturnTheSameResponse_BeforeAndAfterAnItemOnItIsReserved()
    {
        // Arrange
        var listId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var shareToken = ShareTokens.New();
        await PublishListAsync(listId, ownerId, shareToken, itemId);
        var accessToken = TestTokenIssuer.IssueAccessToken(ownerId);
        var before = await WaitForOwnerListAsync(listId, accessToken, list => list.GetProperty("items").GetArrayLength() == 1);

        // Act — reserve, and wait until the guest view has seen it so "after" is really after
        await gateway.ReservationsBus.Publish(new GiftReservedV1(listId, itemId, DateTimeOffset.UtcNow));
        await Eventually.Async(
            async () =>
            {
                var response = await GraphQlClient.QueryAsync(
                    gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken });
                return response.Data?.GetProperty("sharedGiftList").GetProperty("items").EnumerateArray()
                    .Any(i => i.GetProperty("reserved").GetBoolean()) ?? false;
            },
            reservedSeen => reservedSeen,
            WaitTimeout);
        var after = await WaitForOwnerListAsync(listId, accessToken, _ => true);

        // Assert
        Assert.Equal(before.GetRawText(), after.GetRawText());
    }

    private async Task PublishListAsync(Guid listId, Guid ownerId, string shareToken, Guid itemId)
    {
        await gateway.GiftListsBus.Publish(new GiftListCreatedV1(
            listId, ownerId, "Birthday Wishlist", DateTimeOffset.UtcNow.AddDays(7), shareToken, DateTimeOffset.UtcNow));
        await gateway.GiftListsBus.Publish(new GiftItemAddedV1(
            listId, itemId, "Lego Set", "The big one", "https://example.test/lego", DateTimeOffset.UtcNow));
    }

    private async Task<JsonElement> WaitForOwnerListAsync(Guid listId, string accessToken, Func<JsonElement, bool> condition) =>
        await Eventually.Async(
            async () =>
            {
                var response = await GraphQlClient.QueryAsync(
                    gateway.GraphQlHttpClient, GiftListGraphQlQueries.GiftList, new { id = listId }, accessToken);
                if (response.Data is not { } data)
                {
                    return (JsonElement?)null;
                }

                var list = data.GetProperty("giftList");
                return list.ValueKind == JsonValueKind.Null ? null : list;
            },
            list => list is { } ownerList && condition(ownerList),
            WaitTimeout) ?? throw new InvalidOperationException("unreachable");
}
