using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Results;
using Rebus.Bus;
using Rebus.Handlers;
using Reservations.Contracts.Reservations;
using Reservations.Contracts.Reservations.Events;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Stands in for Reservations on the real broker (CONVENTIONS.md "Testing": "other services are
/// not run — assert on contracts published to the real broker"). Handles the exact wire type
/// Reservations' own <c>ReserveGiftHandler</c> does — <see cref="ReserveGift"/> in,
/// <see cref="ReserveGiftReply"/> or <c>ReplyFault</c> out — so <c>ReservationsGrpcService</c> is
/// exercised against the real request/reply bridge end to end, mirroring
/// <see cref="FakeIdentityResponder"/>'s own shape exactly. On a successful reservation it also
/// publishes the real <see cref="GiftReservedV1"/> Reservations itself would (the same event
/// <c>GiftReservedV1Handler</c> already builds the Gateway's own reservation projection from), so
/// a full round trip — reserve, then see <c>reserved: true</c> through <c>sharedGiftList(token)</c>
/// — can be proved without a second, hand-rolled publish step per test.
///
/// The six well-known item ids below let a test choose Reservations' answer deterministically
/// without needing a second, real gift-list projection inside Reservations itself (which is not
/// running) — the exact same "sentinel value picks the branch" trick
/// <see cref="FakeIdentityResponder"/> uses for its well-known emails. <see cref="Guid.Empty"/> is
/// not one of them: it is deliberately left to fall through to the same
/// <c>reservation.invalid_id</c> reply Reservations' own <c>ReserveGiftValidator</c> would give,
/// so an empty item id reaching this fake (a well-formed but rejectable Guid — GL-37,
/// <c>ReservationsGrpcService.ParseId</c>'s own remarks) is what proves that code's mapping, not
/// merely something asserted about this fixture's own behaviour.
/// </summary>
public sealed class FakeReservationsResponder(IBus bus) : IHandleMessages<ReserveGift>
{
    public static readonly Guid AlreadyReservedItemId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    public static readonly Guid GiftListNotFoundItemId = Guid.Parse("11111111-0000-0000-0000-000000000002");
    public static readonly Guid GiftListDeletedItemId = Guid.Parse("11111111-0000-0000-0000-000000000003");
    public static readonly Guid GiftListExpiredItemId = Guid.Parse("11111111-0000-0000-0000-000000000004");
    public static readonly Guid GiftItemNotFoundItemId = Guid.Parse("11111111-0000-0000-0000-000000000005");

    public static readonly Error AlreadyReserved = new(
        "reservation.already_reserved", "This gift has already been reserved.", ErrorKind.Conflict);

    public static readonly Error GiftListNotFound = new(
        "reservation.giftlist_not_found", "No such gift list exists.", ErrorKind.NotFound);

    public static readonly Error GiftListDeleted = new(
        "reservation.giftlist_deleted", "This gift list no longer exists.", ErrorKind.NotFound);

    public static readonly Error GiftListExpired = new(
        "reservation.giftlist_expired", "This gift list has expired and can no longer accept reservations.", ErrorKind.Conflict);

    public static readonly Error GiftItemNotFound = new(
        "reservation.giftitem_not_found", "No such gift item exists on this list.", ErrorKind.NotFound);

    public static readonly Error InvalidId = new(
        "reservation.invalid_id", "A required identifier was missing or invalid.", ErrorKind.Validation);

    public async Task Handle(ReserveGift message)
    {
        if (message.ListId == Guid.Empty || message.ItemId == Guid.Empty)
        {
            await bus.Reply(ReplyFault.From(InvalidId));
            return;
        }

        var fault = message.ItemId switch
        {
            var id when id == AlreadyReservedItemId => AlreadyReserved,
            var id when id == GiftListNotFoundItemId => GiftListNotFound,
            var id when id == GiftListDeletedItemId => GiftListDeleted,
            var id when id == GiftListExpiredItemId => GiftListExpired,
            var id when id == GiftItemNotFoundItemId => GiftItemNotFound,
            _ => (Error?)null,
        };

        if (fault is { } error)
        {
            await bus.Reply(ReplyFault.From(error));
            return;
        }

        // Deterministic, plausible-looking secret — long enough that a test can tell it apart
        // from an accidental substring match, same reasoning as FakeIdentityResponder's token.
        var releaseSecret = $"release-secret-{message.ListId:N}-{message.ItemId:N}";
        var reservedAt = DateTimeOffset.UtcNow;
        await bus.Reply(new ReserveGiftReply(releaseSecret, reservedAt));

        // Mirrors the real ReserveGiftInteractor's own "save first, publish second"
        // (ARCHITECTURE.md "Event publishing: synchronous") — the reply above is the "save", this
        // is the publish, and only ever follows a successful reply, never a faulted one.
        await bus.Publish(new GiftReservedV1(message.ListId, message.ItemId, reservedAt));
    }
}
