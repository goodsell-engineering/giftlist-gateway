using Gateway.Infrastructure.GiftLists.Grpc;
using Gateway.Infrastructure.Platform.Transport;
using Gateway.IntegrationTests.Fixtures;
using Gateway.IntegrationTests.Support;
using Grpc.Core;

namespace Gateway.IntegrationTests.GiftLists;

/// <summary>
/// End to end through the real grpc-web pipeline (GL-71): a real grpc-web client, over HTTP,
/// into the real ASP.NET Core host, through <c>GiftListsGrpcService</c>, onto the real broker —
/// a <see cref="GiftListsCommandListener"/> stands in for GiftLists (CONVENTIONS.md "Testing": "other
/// services are not run"), recording what was actually sent rather than what the RPC merely
/// claims to have sent.
///
/// Covers the issue's two hard requirements directly:
/// <list type="bullet">
/// <item>every command's Owner/RequesterId is the caller's own JWT subject, never anything the
/// wire message could carry (there is no such field to carry it — see
/// <see cref="GiftListsProtoContractTests"/> for the companion proof that no message has one);</item>
/// <item>every RPC requires authentication — <c>RequireAuthorization()</c>
/// (<c>GatewayGrpcEndpointRouteBuilderExtensions</c>) is the only thing standing between an
/// anonymous caller and the bus, since <c>Program.cs</c>'s bare <c>app.UseAuthorization()</c>
/// enforces nothing by default.</item>
/// </list>
///
/// Also covers the GL-71 Batch 14 review findings: an empty/malformed <c>list_id</c> and an
/// omitted <c>expires_at</c> both fail as <c>INVALID_ARGUMENT</c> with a specific error-code
/// trailer, not an unmapped <c>Unknown</c> status (B1/B2); and a validly-signed token whose
/// <c>sub</c> is not a <see cref="Guid"/> fails as <c>UNAUTHENTICATED</c> (S3) — the one branch
/// of <c>ServerCallContextExtensions.RequireUserId</c>'s guard that can fire once
/// <c>RequireAuthorization()</c> is in place.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class GiftListsGrpcServiceTests(GatewayFixture gateway)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task CreateGiftList_ShouldSendTheCommandWithTheCallersOwnIdAsOwnerId_AndReturnTheGeneratedListId()
    {
        // Arrange — the JWT's subject is the only source of OwnerId; CreateGiftListRequest
        // carries no such field at all for a malicious caller to populate instead.
        var ownerId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var request = new CreateGiftListRequest { Name = "Birthday Wishlist" };
        request.ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(expiresAt);

        // Act
        var response = await gateway.GiftListsClient.CreateGiftListAsync(request, AuthHeaders(ownerId)).ResponseAsync;
        var listId = Guid.Parse(response.ListId);
        var command = await WaitForAsync(() => gateway.GiftListsCommands.CreateGiftLists.FirstOrDefault(c => c.ListId == listId));

        // Assert
        Assert.Equal(ownerId, command!.OwnerId);
        Assert.Equal("Birthday Wishlist", command.Name);
        Assert.Equal(expiresAt, command.ExpiresAt);
    }

    [Fact]
    public async Task CreateGiftList_ShouldFailWithUnauthenticated_WhenNoAccessTokenIsSent()
    {
        // Arrange — no Authorization header at all.
        var request = new CreateGiftListRequest
        {
            Name = "Birthday Wishlist",
            ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(7)),
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.GiftListsClient.CreateGiftListAsync(request).ResponseAsync);

        // Assert
        AssertRejectedByEndpointAuthorization(exception);
    }

    [Fact]
    public async Task CreateGiftList_ShouldFailWithInvalidArgument_WhenExpiresAtIsOmitted()
    {
        // Arrange — proto3 message fields carry presence but cannot be marked required, so an
        // omitted ExpiresAt deserializes to null rather than a default Timestamp (GL-71 Batch 14
        // review, B2) — no ExpiresAt assignment at all, deliberately.
        var request = new CreateGiftListRequest { Name = "Birthday Wishlist" };

        // Act
        var exception = await Record.ExceptionAsync(
            () => gateway.GiftListsClient.CreateGiftListAsync(request, AuthHeaders(Guid.NewGuid())).ResponseAsync);

        // Assert — INVALID_ARGUMENT with the specific code, not the unmapped Unknown status a
        // bare NullReferenceException surfaces as.
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.InvalidArgument, rpcException.StatusCode);
        Assert.Equal("gateway.missing_expiry", rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task RenameGiftList_ShouldSendTheCommandWithTheCallersOwnIdAsRequesterId_NeverAWireSuppliedValue()
    {
        // Arrange — RenameGiftListRequest has no requester field (see
        // GiftListsProtoContractTests), so the only way a RequesterId reaches the command below
        // is off this caller's own token.
        var requesterId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var request = new RenameGiftListRequest { ListId = listId.ToString(), Name = "New Name" };

        // Act
        await gateway.GiftListsClient.RenameGiftListAsync(request, AuthHeaders(requesterId)).ResponseAsync;
        var command = await WaitForAsync(() => gateway.GiftListsCommands.RenameGiftLists.FirstOrDefault(c => c.ListId == listId));

        // Assert
        Assert.Equal(requesterId, command!.RequesterId);
        Assert.Equal("New Name", command.Name);
    }

    [Fact]
    public async Task RenameGiftList_ShouldFailWithUnauthenticated_WhenNoAccessTokenIsSent()
    {
        // Arrange
        var request = new RenameGiftListRequest { ListId = Guid.NewGuid().ToString(), Name = "New Name" };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.GiftListsClient.RenameGiftListAsync(request).ResponseAsync);

        // Assert
        AssertRejectedByEndpointAuthorization(exception);
    }

    [Fact]
    public async Task RenameGiftList_ShouldFailWithInvalidArgument_WhenListIdIsEmpty()
    {
        // Arrange — GL-71 Batch 14 review (B1): an empty (or otherwise malformed) list_id
        // previously reached a bare Guid.Parse and surfaced as an unmapped Unknown status with
        // no error-code trailer.
        var request = new RenameGiftListRequest { ListId = "", Name = "New Name" };

        // Act
        var exception = await Record.ExceptionAsync(
            () => gateway.GiftListsClient.RenameGiftListAsync(request, AuthHeaders(Guid.NewGuid())).ResponseAsync);

        // Assert
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.InvalidArgument, rpcException.StatusCode);
        Assert.Equal("gateway.invalid_id", rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    [Fact]
    public async Task DeleteGiftList_ShouldSendTheCommandWithTheCallersOwnIdAsRequesterId()
    {
        // Arrange
        var requesterId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var request = new DeleteGiftListRequest { ListId = listId.ToString() };

        // Act
        await gateway.GiftListsClient.DeleteGiftListAsync(request, AuthHeaders(requesterId)).ResponseAsync;
        var command = await WaitForAsync(() => gateway.GiftListsCommands.DeleteGiftLists.FirstOrDefault(c => c.ListId == listId));

        // Assert
        Assert.Equal(requesterId, command!.RequesterId);
    }

    [Fact]
    public async Task DeleteGiftList_ShouldFailWithUnauthenticated_WhenNoAccessTokenIsSent()
    {
        // Arrange
        var request = new DeleteGiftListRequest { ListId = Guid.NewGuid().ToString() };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.GiftListsClient.DeleteGiftListAsync(request).ResponseAsync);

        // Assert
        AssertRejectedByEndpointAuthorization(exception);
    }

    [Fact]
    public async Task AddGiftItem_ShouldSendTheCommandWithTheCallersOwnIdAsRequesterId_AndReturnTheGeneratedItemId()
    {
        // Arrange
        var requesterId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var request = new AddGiftItemRequest
        {
            ListId = listId.ToString(),
            Name = "Lego Set",
            Description = "The big one",
            Url = "https://example.test/lego",
        };

        // Act
        var response = await gateway.GiftListsClient.AddGiftItemAsync(request, AuthHeaders(requesterId)).ResponseAsync;
        var itemId = Guid.Parse(response.ItemId);
        var command = await WaitForAsync(() => gateway.GiftListsCommands.AddGiftItems.FirstOrDefault(c => c.ItemId == itemId));

        // Assert
        Assert.Equal(requesterId, command!.RequesterId);
        Assert.Equal(listId, command.ListId);
        Assert.Equal("Lego Set", command.Name);
        Assert.Equal("The big one", command.Description);
        Assert.Equal("https://example.test/lego", command.Url);
    }

    [Fact]
    public async Task AddGiftItem_ShouldFailWithUnauthenticated_WhenNoAccessTokenIsSent()
    {
        // Arrange
        var request = new AddGiftItemRequest { ListId = Guid.NewGuid().ToString(), Name = "Lego Set" };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.GiftListsClient.AddGiftItemAsync(request).ResponseAsync);

        // Assert
        AssertRejectedByEndpointAuthorization(exception);
    }

    [Fact]
    public async Task RemoveGiftItem_ShouldSendTheCommandWithTheCallersOwnIdAsRequesterId()
    {
        // Arrange
        var requesterId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var request = new RemoveGiftItemRequest { ListId = listId.ToString(), ItemId = itemId.ToString() };

        // Act
        await gateway.GiftListsClient.RemoveGiftItemAsync(request, AuthHeaders(requesterId)).ResponseAsync;
        var command = await WaitForAsync(() => gateway.GiftListsCommands.RemoveGiftItems.FirstOrDefault(c => c.ItemId == itemId));

        // Assert
        Assert.Equal(requesterId, command!.RequesterId);
        Assert.Equal(listId, command.ListId);
    }

    [Fact]
    public async Task RemoveGiftItem_ShouldFailWithUnauthenticated_WhenNoAccessTokenIsSent()
    {
        // Arrange
        var request = new RemoveGiftItemRequest { ListId = Guid.NewGuid().ToString(), ItemId = Guid.NewGuid().ToString() };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.GiftListsClient.RemoveGiftItemAsync(request).ResponseAsync);

        // Assert
        AssertRejectedByEndpointAuthorization(exception);
    }

    /// <summary>
    /// The issue's central security scenario: a caller who does not own <paramref name="listId"/>
    /// cannot make the resulting command carry anyone's id but their own — there is no field on
    /// the wire (<see cref="RenameGiftListRequest"/>/<see cref="DeleteGiftListRequest"/>) they
    /// could set to impersonate the real owner. GiftLists itself (not run here, CONVENTIONS.md
    /// "Testing") is what would ultimately reject such a command as Forbidden, comparing RequesterId
    /// against the list's real OwnerId — this test proves the Gateway leaves it nothing to
    /// compare against but the caller's own identity.
    /// </summary>
    [Fact]
    public async Task RenameGiftList_ShouldCarryEachCallersOwnId_WhenTwoDifferentCallersTargetTheSameList()
    {
        // Arrange — listId "belongs" to nobody as far as the Gateway is concerned; two different
        // callers both attempt to rename it.
        var listId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var impostor = Guid.NewGuid();
        Assert.NotEqual(owner, impostor);

        // Act
        await gateway.GiftListsClient.RenameGiftListAsync(
            new RenameGiftListRequest { ListId = listId.ToString(), Name = "Owner's rename" },
            AuthHeaders(owner)).ResponseAsync;
        await gateway.GiftListsClient.RenameGiftListAsync(
            new RenameGiftListRequest { ListId = listId.ToString(), Name = "Impostor's rename" },
            AuthHeaders(impostor)).ResponseAsync;

        var commands = await WaitForAsync(() =>
        {
            var forThisList = gateway.GiftListsCommands.RenameGiftLists.Where(c => c.ListId == listId).ToList();
            return forThisList.Count == 2 ? forThisList : null;
        });

        // Assert — each command carries its own caller's id, never the other's.
        Assert.Contains(commands!, c => c.RequesterId == owner && c.Name == "Owner's rename");
        Assert.Contains(commands!, c => c.RequesterId == impostor && c.Name == "Impostor's rename");
    }

    /// <summary>
    /// GL-71 Batch 14 review (S3): with <c>RequireAuthorization()</c> in place, an unauthenticated
    /// caller never reaches <c>ServerCallContextExtensions.RequireUserId</c> at all, so its
    /// <c>Identity.IsAuthenticated != true</c> half is dead in production — a validly-signed,
    /// non-expired token whose <c>sub</c> claim is not a <see cref="Guid"/> is the only way the
    /// other half of that guard (<c>!Guid.TryParse(subject, out _)</c>) can actually fire, and
    /// nothing exercised it before this test.
    /// </summary>
    [Fact]
    public async Task RenameGiftList_ShouldFailWithUnauthenticated_WhenTheTokenSubjectIsNotAGuid()
    {
        // Arrange — a valid, correctly-signed token; only the subject claim is not a Guid.
        var request = new RenameGiftListRequest { ListId = Guid.NewGuid().ToString(), Name = "New Name" };
        var headers = new Metadata
        {
            { "authorization", $"Bearer {TestTokenIssuer.IssueAccessTokenWithSubject("not-a-guid")}" },
        };

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.GiftListsClient.RenameGiftListAsync(request, headers).ResponseAsync);

        // Assert — rejected by the handler's own guard (not RequireAuthorization(), which lets a
        // validly-signed token through), so the error-code trailer IS present here, unlike
        // AssertRejectedByEndpointAuthorization's cases.
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Unauthenticated, rpcException.StatusCode);
        Assert.Equal("gateway.unauthenticated", rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }

    private static Metadata AuthHeaders(Guid userId) =>
        new() { { "authorization", $"Bearer {TestTokenIssuer.IssueAccessToken(userId)}" } };

    private static async Task<T> WaitForAsync<T>(Func<T?> poll) where T : class =>
        await Eventually.Async(() => Task.FromResult(poll()), result => result is not null, WaitTimeout)
        ?? throw new InvalidOperationException("unreachable");

    /// <summary>
    /// Asserts the call was rejected by <c>RequireAuthorization()</c>
    /// (<c>GatewayGrpcEndpointRouteBuilderExtensions</c>) — i.e. before
    /// <c>GiftListsGrpcService</c> ever ran — rather than by that method's own
    /// <c>ServerCallContextExtensions.RequireUserId</c> guard, which would reject an
    /// unauthenticated caller anyway and so cannot tell the two apart on
    /// <see cref="RpcException.StatusCode"/> alone (both surface
    /// <see cref="StatusCode.Unauthenticated"/>). The ASP.NET Core authorization middleware
    /// short-circuits with a bare, unmapped HTTP 401 that never reaches
    /// <see cref="ErrorToRpcExceptionMapper.ToRpcException"/> — no
    /// <see cref="ErrorToRpcExceptionMapper.ErrorCodeTrailerName"/> trailer, unlike every failure
    /// this codebase raises deliberately — so its absence here is what actually pins
    /// <c>RequireAuthorization()</c>'s own effect: removing it (verified by mutation, GL-71)
    /// leaves this trailer present instead, because the request then reaches the handler and its
    /// guard rejects it there instead, and this assertion goes red.
    /// </summary>
    private static void AssertRejectedByEndpointAuthorization(Exception? exception)
    {
        var rpcException = Assert.IsType<RpcException>(exception);
        Assert.Equal(StatusCode.Unauthenticated, rpcException.StatusCode);
        Assert.Null(rpcException.Trailers.GetValue(ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
    }
}
