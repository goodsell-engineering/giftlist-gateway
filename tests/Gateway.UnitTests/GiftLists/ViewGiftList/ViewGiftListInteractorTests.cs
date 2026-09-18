using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.ViewGiftList;
using Gateway.UnitTests.Support;

namespace Gateway.UnitTests.GiftLists.ViewGiftList;

/// <summary>
/// GL-38: the visibility rule, branch by branch — what an <c>Owner</c> viewer's data looks like
/// versus a <c>GuestWithToken</c>'s, and that the owner branch never asks the reservation
/// projection anything (ARCHITECTURE.md "Defence in depth on the owner-facing path"). The
/// end-to-end shape of both surfaces is Gateway.IntegrationTests' job; this suite is the
/// gap-filler for the one property no GraphQL response can show — a port <em>not</em> being
/// called — and for the branching a real broker makes tedious to provoke (CONVENTIONS.md
/// "Testing"). Reached through the composition root, decorators and all (<see cref="GatewayPorts"/>).
/// </summary>
public sealed class ViewGiftListInteractorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string ShareToken = "aBcDeFgHiJkLmNoPqRsTu";

    [Fact]
    public async Task Handle_ShouldReturnTheOwnerView_WhenTheViewerIsTheOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var list = ListOwnedBy(ownerId, Guid.NewGuid(), Guid.NewGuid());
        var giftLists = new FakeGiftListProjectionRepository();
        giftLists.Seed(list);
        var interactor = GatewayPorts.BuildViewGiftList(giftLists, new FakeReservationProjectionRepository());

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.Owner(list.ListId, ownerId)), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var owner = Assert.IsType<GiftListView.ForOwner>(result.Value.View);
        Assert.Same(list, owner.GiftList);
        Assert.Equal(ownerId, owner.GiftList.OwnerId);
        Assert.Equal(ShareToken, owner.GiftList.ShareToken);
    }

    /// <summary>
    /// THE property this use case exists for. For an owner viewer the reservation projection is
    /// not queried and then hidden — it is not queried. A response-shape assertion cannot show
    /// that; only the port can.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldNeverAskTheReservationProjection_WhenTheViewerIsTheOwner()
    {
        // Arrange — reservations exist on this very list, so "not asked" is not "nothing to find"
        var ownerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var list = ListOwnedBy(ownerId, itemId, Guid.NewGuid());
        var giftLists = new FakeGiftListProjectionRepository();
        giftLists.Seed(list);
        var reservations = new FakeReservationProjectionRepository();
        reservations.Seed(list.ListId, itemId);
        var interactor = GatewayPorts.BuildViewGiftList(giftLists, reservations);

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.Owner(list.ListId, ownerId)), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(reservations.ListsAskedFor);
    }

    [Fact]
    public async Task Handle_ShouldReturnForbiddenAndNotAskTheReservationProjection_WhenTheViewerIsNotTheOwner()
    {
        // Arrange
        var list = ListOwnedBy(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var giftLists = new FakeGiftListProjectionRepository();
        giftLists.Seed(list);
        var reservations = new FakeReservationProjectionRepository();
        var interactor = GatewayPorts.BuildViewGiftList(giftLists, reservations);

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.Owner(list.ListId, Guid.NewGuid())), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(GiftListErrors.Forbidden.Code, result.Error.Code);
        Assert.Empty(reservations.ListsAskedFor);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTheOwnerViewerNamesAListThatDoesNotExist()
    {
        // Arrange
        var interactor = GatewayPorts.BuildViewGiftList(
            new FakeGiftListProjectionRepository(), new FakeReservationProjectionRepository());

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.Owner(Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(GiftListErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheGuestViewWithReservedFlags_WhenTheViewerHoldsTheToken()
    {
        // Arrange — two items, one reserved
        var reservedItemId = Guid.NewGuid();
        var freeItemId = Guid.NewGuid();
        var list = ListOwnedBy(Guid.NewGuid(), reservedItemId, freeItemId);
        var giftLists = new FakeGiftListProjectionRepository();
        giftLists.Seed(list);
        var reservations = new FakeReservationProjectionRepository();
        reservations.Seed(list.ListId, reservedItemId);
        var interactor = GatewayPorts.BuildViewGiftList(giftLists, reservations);

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.GuestWithToken(ShareToken)), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var guest = Assert.IsType<GiftListView.ForGuest>(result.Value.View);
        Assert.Equal(list.ListId, guest.GiftList.ListId);
        Assert.Equal(list.Name, guest.GiftList.Name);
        Assert.Equal(list.ExpiresAt, guest.GiftList.ExpiresAt);
        Assert.Collection(guest.GiftList.Items,
            item =>
            {
                Assert.Equal(reservedItemId, item.ItemId);
                Assert.True(item.Reserved);
            },
            item =>
            {
                Assert.Equal(freeItemId, item.ItemId);
                Assert.False(item.Reserved);
            });
        Assert.Equal([list.ListId], reservations.ListsAskedFor);
    }

    /// <summary>
    /// The guest's item type is the only place a reservation fact is ever surfaced, and it is a
    /// boolean. Pinned by reflection so that a field added to <see cref="SharedGiftItemView"/>
    /// — a <c>reservedAt</c>, a reservation id, anything a viewer could correlate on — fails a
    /// test that names the rule, not just a GraphQL snapshot somewhere (ARCHITECTURE.md "Nobody
    /// can see *who* reserved"). Mirrors <c>Reservations.UnitTests</c>' <c>GiftReservedV1Tests</c>
    /// for the wire type.
    /// </summary>
    [Fact]
    public void SharedGiftItemView_ShouldCarryReservedAsABooleanAndNothingElseAboutAReservation()
    {
        // Arrange
        var expected = new Dictionary<string, Type>
        {
            [nameof(SharedGiftItemView.ItemId)] = typeof(Guid),
            [nameof(SharedGiftItemView.Name)] = typeof(string),
            [nameof(SharedGiftItemView.Description)] = typeof(string),
            [nameof(SharedGiftItemView.Url)] = typeof(string),
            [nameof(SharedGiftItemView.Reserved)] = typeof(bool),
        };

        // Act
        var actual = typeof(SharedGiftItemView).GetProperties()
            .Where(p => p.Name != "EqualityContract")
            .ToDictionary(p => p.Name, p => p.PropertyType);

        // Assert
        Assert.Equal(expected.OrderBy(kv => kv.Key), actual.OrderBy(kv => kv.Key));
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTheTokenResolvesToNoList()
    {
        // Arrange — well-formed, and belonging to nothing
        var reservations = new FakeReservationProjectionRepository();
        var interactor = GatewayPorts.BuildViewGiftList(new FakeGiftListProjectionRepository(), reservations);

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.GuestWithToken(ShareToken)), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(GiftListErrors.NotFound.Code, result.Error.Code);
        Assert.Empty(reservations.ListsAskedFor);
    }

    [Fact]
    public async Task Handle_ShouldReturnUnauthenticatedAndNotAskTheReservationProjection_WhenTheViewerIsAnonymous()
    {
        // Arrange
        var reservations = new FakeReservationProjectionRepository();
        var interactor = GatewayPorts.BuildViewGiftList(new FakeGiftListProjectionRepository(), reservations);

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.Anonymous()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(GiftListErrors.Unauthenticated.Code, result.Error.Code);
        Assert.Empty(reservations.ListsAskedFor);
    }

    /// <summary>Through the real Validating decorator — proves the validator is wired, not just that it exists.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123")]
    [InlineData("0123456789abcdef01234\n")]
    public async Task Handle_ShouldReturnInvalidShareToken_WhenTheGuestTokenIsMalformed(string token)
    {
        // Arrange
        var giftLists = new FakeGiftListProjectionRepository();
        giftLists.Seed(ListOwnedBy(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        var interactor = GatewayPorts.BuildViewGiftList(giftLists, new FakeReservationProjectionRepository());

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.GuestWithToken(token)), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(GiftListErrors.InvalidShareToken.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidId_WhenTheOwnerViewerCarriesAnEmptyId()
    {
        // Arrange
        var interactor = GatewayPorts.BuildViewGiftList(
            new FakeGiftListProjectionRepository(), new FakeReservationProjectionRepository());

        // Act
        var result = await interactor.Handle(
            new ViewGiftListRequest(new ViewerContext.Owner(Guid.Empty, Guid.NewGuid())), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(GiftListErrors.InvalidId.Code, result.Error.Code);
    }

    private static GiftListProjection ListOwnedBy(Guid ownerId, Guid firstItemId, Guid secondItemId) => new(
        Guid.NewGuid(),
        ownerId,
        "Birthday Wishlist",
        Now.AddDays(7),
        ShareToken,
        Now,
        [
            new GiftItemProjection(firstItemId, "Lego Set", "The big one", "https://example.test/lego"),
            new GiftItemProjection(secondItemId, "Coffee grinder", null, null),
        ]);
}
