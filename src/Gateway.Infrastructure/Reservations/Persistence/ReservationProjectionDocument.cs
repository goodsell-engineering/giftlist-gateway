namespace Gateway.Infrastructure.Reservations.Persistence;

/// <summary>
/// The Mongo-facing shape of <c>gateway.reservationProjections</c> — one document per reserved
/// (list, item) pair, kept separate from <c>Gateway.Application.Reservations.ReservationProjection</c>
/// (ARCHITECTURE.md "Data that crosses boundaries" — no <c>[Bson*]</c> attributes reach
/// Application; none are needed here either, <see cref="Id"/> auto-maps to <c>_id</c> and every
/// field camelCases via the shared <c>BuildingBlocks.Persistence.MongoConventions</c> pack).
/// </summary>
/// <remarks>
/// <para>
/// A separate collection from <c>giftListProjections</c>, joined only inside the one interactor
/// that may read both (ARCHITECTURE.md "Data model", "Defence in depth on the owner-facing path"
/// — "read-model separation"). The owner-facing queries read a collection that structurally
/// cannot contain reservation state.
/// </para>
/// <para>
/// Three fields and no more. <see cref="Id"/> is the (list, item) pair itself, so the upsert in
/// <see cref="ReservationProjectionRepository"/> is keyed on the entity the event is about
/// (CONVENTIONS.md "Messaging") and a redelivered <c>GiftReservedV1</c> lands on the same
/// document. There is no <c>reservedAt</c>, though the event carries one, and no version counter:
/// the document has no mutable field, so there is no last-write-wins to resolve and no
/// compare-and-set to retry — a reserved pair is reserved, and this collection holds no other
/// fact. <c>ReservationProjection</c>'s own doc comment has the privacy half of that reasoning.
/// </para>
/// </remarks>
public sealed class ReservationProjectionDocument
{
    /// <summary>The (list, item) pair, as <c>{listId:N}:{itemId:N}</c> — see <see cref="ReservationProjectionRepository"/>.</summary>
    public required string Id { get; init; }

    public required Guid ListId { get; init; }

    public required Guid ItemId { get; init; }
}
