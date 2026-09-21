using Gateway.Infrastructure.Reservations.Grpc;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gateway.IntegrationTests.Reservations;

/// <summary>
/// GL-37: proves ARCHITECTURE.md "Reservation privacy"'s guarantee about the release secret end
/// to end on the Gateway side, against the real infrastructure it is supposed to hold against
/// (CONVENTIONS.md "Testing") — the mirror image of
/// <c>Reservations.IntegrationTests.Reservations.ReleaseSecretPrivacyTests</c> from
/// giftlist-reservations PR #8. That suite proved the secret is returned to the reserving caller
/// and stored in Reservations' own <c>reservation.reservations</c> collection, but never anywhere
/// else Reservations can write to or log from. This file proves the other half of the same hop:
/// the Gateway receives the secret in exactly one place — the RPC response — and it does not leak
/// from there into this service's own logs, its own database, or any GraphQL payload.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class ReleaseSecretPrivacyTests(GatewayFixture gateway) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReserveGift_ShouldNeverStoreTheReleaseSecretInAnyGatewayCollection()
    {
        // Arrange
        var (shareToken, itemId) = await ReservableGiftLists.CreateAsync(gateway);

        // Act
        var response = await gateway.ReservationsClient.ReserveGiftAsync(
            new ReserveGiftRequest { ShareToken = shareToken, ItemId = itemId.ToString() }).ResponseAsync;
        var secret = response.ReleaseSecret;

        // Assert — the Gateway has no reservations collection of its own at all (that document
        // lives in Reservations' database, ARCHITECTURE.md "Data model"); this scans every
        // collection in the Gateway's own database generically, rather than naming the ones that
        // happen to exist today, so a future projection is covered by construction rather than by
        // someone remembering to extend this test (mirrors the Reservations-side test's own
        // reasoning exactly).
        var database = gateway.Database;
        var collectionNames = await (await database.ListCollectionNamesAsync()).ToListAsync();
        foreach (var collectionName in collectionNames)
        {
            var documents = await database
                .GetCollection<BsonDocument>(collectionName)
                .Find(FilterDefinition<BsonDocument>.Empty)
                .ToListAsync();
            foreach (var document in documents)
            {
                Assert.DoesNotContain(secret, document.ToJson(), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// <c>ReservationsGrpcService</c> is a plain grpc-web adapter, not an <c>IInteractor&lt;,&gt;</c>
    /// — like <c>AuthGrpcService</c>, it is never wrapped by the <c>Logging&lt;,&gt;</c> decorator
    /// (that decorator only ever wraps the open generic every use case is registered against;
    /// this RPC has no local use case to register — see that class's own remarks), so there is no
    /// decorator payload to inspect here the way
    /// <c>Reservations.IntegrationTests.Reservations.ReleaseSecretPrivacyTests</c> inspects
    /// <c>ReserveGiftInteractor</c>'s. <c>BuildingBlocks.Testing.LogCapture</c> is the actual
    /// backstop on this side instead — it sees every log call made anywhere in the real Gateway host under test,
    /// so this is at least as strong a guarantee as a decorator-specific assertion would be, not a
    /// narrower one.
    /// </summary>
    [Fact]
    public async Task ReserveGift_ShouldNeverLogTheReleaseSecretsPlaintext()
    {
        // Arrange
        var (shareToken, itemId) = await ReservableGiftLists.CreateAsync(gateway);

        // Act
        var response = await gateway.ReservationsClient.ReserveGiftAsync(
            new ReserveGiftRequest { ShareToken = shareToken, ItemId = itemId.ToString() }).ResponseAsync;
        var secret = response.ReleaseSecret;

        // Assert
        Assert.DoesNotContain(gateway.Logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    /// <summary>
    /// The full round trip GL-37 exists to prove: reserve through the RPC, then see
    /// <c>reserved: true</c> — and nothing resembling the secret — through the guest's own read
    /// surface. <c>FakeReservationsResponder</c> publishes the real <c>GiftReservedV1</c> a
    /// successful reservation would (its own doc comment), so this exercises the Gateway's real
    /// <c>GiftReservedV1Handler</c>/reservation projection/<c>sharedGiftList(token)</c> resolver
    /// chain end to end, not a fixture shortcut.
    /// </summary>
    [Fact]
    public async Task ReserveGift_ShouldShowReservedTrue_AndNeverTheSecret_ThroughTheGuestView()
    {
        // Arrange
        var (shareToken, itemId) = await ReservableGiftLists.CreateAsync(gateway);

        // Act
        var response = await gateway.ReservationsClient.ReserveGiftAsync(
            new ReserveGiftRequest { ShareToken = shareToken, ItemId = itemId.ToString() }).ResponseAsync;
        var secret = response.ReleaseSecret;

        var sharedList = await Eventually.Async(
            () => GraphQlClient.QueryAsync(gateway.GraphQlHttpClient, GiftListGraphQlQueries.SharedGiftList, new { token = shareToken }),
            result => result.Data is { } data
                && data.GetProperty("sharedGiftList").GetProperty("items").EnumerateArray()
                    .Any(item => item.GetProperty("reserved").GetBoolean()),
            WaitTimeout);

        // Assert
        var items = sharedList.Data!.Value.GetProperty("sharedGiftList").GetProperty("items");
        var item = Assert.Single(items.EnumerateArray(), i => i.GetProperty("itemId").GetGuid() == itemId);
        Assert.True(item.GetProperty("reserved").GetBoolean());
        Assert.DoesNotContain(secret, items.GetRawText(), StringComparison.Ordinal);
    }
}
