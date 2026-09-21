using Gateway.Infrastructure.Platform.Transport;
using Gateway.Infrastructure.Reservations.Grpc;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using Grpc.Core;

namespace Gateway.IntegrationTests.Reservations;

/// <summary>
/// GL-37: end to end through the real grpc-web pipeline, the same shape as
/// <c>AuthGrpcServiceTests</c>/<c>GiftListsGrpcServiceTests</c> — a real grpc-web client, over
/// HTTP, into the real ASP.NET Core host, through <c>ReservationsGrpcService</c>, across the real
/// request/reply bridge, against a real Rebus handler standing in for Reservations
/// (<see cref="FakeReservationsResponder"/>). This is the Gateway half of the guarantee GL-37
/// proves on the Reservations side (giftlist-reservations PR #8): the hop "bridge reply -&gt; RPC
/// response -&gt; nowhere else".
///
/// Every <c>reservation.*</c> error code below reaches the browser as a specific,
/// distinguishable gRPC status via the same generic <see cref="ErrorToRpcExceptionMapper"/> every
/// other Gateway grpc-web endpoint uses — no per-code switch was added for this RPC, and these
/// tests are what proves that generic mapping actually carries each of the six codes end to end,
/// not merely the two (Conflict/Unauthenticated) <c>AuthGrpcServiceTests</c> already covered.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class ReservationsGrpcServiceTests(GatewayFixture gateway) : IAsyncLifetime
{
    public Task InitializeAsync() => gateway.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReserveGift_ShouldReturnAReleaseSecret_WhenTheItemIsReservable()
    {
        // Arrange
        var (shareToken, itemId) = await CreateReservableListAsync();

        // Act
        var response = await gateway.ReservationsClient.ReserveGiftAsync(
            new ReserveGiftRequest { ShareToken = shareToken, ItemId = itemId.ToString() }).ResponseAsync;

        // Assert — a plausible-looking, non-empty secret; FakeReservationsResponder's own exact
        // format is asserted nowhere else, since the RPC response is only required to carry
        // through whatever Reservations itself minted (ReleaseSecretPrivacyTests covers what it
        // must NOT do with that value once it has it).
        Assert.False(string.IsNullOrWhiteSpace(response.ReleaseSecret));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithInvalidArgument_WhenTheShareTokenIsMalformed()
    {
        // Arrange — CONVENTIONS.md "Errors": Validation -> INVALID_ARGUMENT. Not 21 characters,
        // same boundary rule ViewGiftListValidator enforces on sharedGiftList(token).
        var request = new ReserveGiftRequest { ShareToken = "too-short", ItemId = Guid.NewGuid().ToString() };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.InvalidArgument, rpcException.StatusCode);
        Assert.Equal("gateway.invalid_share_token", rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithNotFound_WhenTheShareTokenResolvesToNoList()
    {
        // Arrange — well-formed (21 base62 characters), but the Gateway's own projection has
        // never heard of it: no GiftListCreatedV1 was ever published for it.
        var request = new ReserveGiftRequest { ShareToken = ShareTokens.New(), ItemId = Guid.NewGuid().ToString() };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.NotFound, rpcException.StatusCode);
        Assert.Equal("gateway.not_found", rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithInvalidArgument_WhenTheItemIdIsMalformed()
    {
        // Arrange — well-formed share token that resolves to a real list; the item id itself is
        // the thing that's wrong (not a Guid at all, unlike ReserveGift_...ItemIdIsEmpty below,
        // which is a well-formed but rejectable Guid).
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest { ShareToken = shareToken, ItemId = "not-a-guid" };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.InvalidArgument, rpcException.StatusCode);
        Assert.Equal("gateway.invalid_id", rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithAborted_WhenReservationsReportsAlreadyReserved()
    {
        // Arrange — CONVENTIONS.md "Errors": Conflict -> ABORTED. FakeReservationsResponder's
        // well-known item id stands in for Reservations' own unique-index race (GL-35), which
        // this fixture cannot provoke for real without running Reservations itself.
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest
        {
            ShareToken = shareToken, ItemId = FakeReservationsResponder.AlreadyReservedItemId.ToString(),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Aborted, rpcException.StatusCode);
        Assert.Equal(
            FakeReservationsResponder.AlreadyReserved.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithNotFound_WhenReservationsReportsGiftListNotFound()
    {
        // Arrange
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest
        {
            ShareToken = shareToken, ItemId = FakeReservationsResponder.GiftListNotFoundItemId.ToString(),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.NotFound, rpcException.StatusCode);
        Assert.Equal(
            FakeReservationsResponder.GiftListNotFound.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithNotFound_WhenReservationsReportsGiftListDeleted()
    {
        // Arrange
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest
        {
            ShareToken = shareToken, ItemId = FakeReservationsResponder.GiftListDeletedItemId.ToString(),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.NotFound, rpcException.StatusCode);
        Assert.Equal(
            FakeReservationsResponder.GiftListDeleted.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithAborted_WhenReservationsReportsGiftListExpired()
    {
        // Arrange — CONVENTIONS.md "Errors": Conflict -> ABORTED, the same flavour as
        // already_reserved (ReservationErrors.GiftListExpired's own doc comment on the
        // Reservations side).
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest
        {
            ShareToken = shareToken, ItemId = FakeReservationsResponder.GiftListExpiredItemId.ToString(),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Aborted, rpcException.StatusCode);
        Assert.Equal(
            FakeReservationsResponder.GiftListExpired.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task ReserveGift_ShouldFailWithNotFound_WhenReservationsReportsGiftItemNotFound()
    {
        // Arrange
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest
        {
            ShareToken = shareToken, ItemId = FakeReservationsResponder.GiftItemNotFoundItemId.ToString(),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.NotFound, rpcException.StatusCode);
        Assert.Equal(
            FakeReservationsResponder.GiftItemNotFound.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    /// <summary>
    /// <c>ReservationsGrpcService.ParseId</c>'s own remarks: <see cref="Guid.Empty"/> is a
    /// well-formed Guid, so the Gateway deliberately does not pre-empt it — this proves the value
    /// reaches Reservations' own <c>ReserveGiftValidator</c> for real (mirrored by
    /// <c>Reservations.IntegrationTests.Reservations.ReserveGiftTests.ReserveGift_ShouldReturnInvalidId_WhenTheListIdIsEmpty</c>
    /// on the other side of this same bridge) rather than being asserted only against this
    /// fixture's own fake.
    /// </summary>
    [Fact]
    public async Task ReserveGift_ShouldFailWithInvalidArgument_WhenTheItemIdIsEmpty()
    {
        // Arrange
        var (shareToken, _) = await CreateReservableListAsync();
        var request = new ReserveGiftRequest { ShareToken = shareToken, ItemId = Guid.Empty.ToString() };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.ReservationsClient.ReserveGiftAsync(request).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.InvalidArgument, rpcException.StatusCode);
        Assert.Equal(
            FakeReservationsResponder.InvalidId.Code,
            rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    private Task<(string ShareToken, Guid ItemId)> CreateReservableListAsync() => ReservableGiftLists.CreateAsync(gateway);
}
