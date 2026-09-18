namespace Gateway.Application.Reservations;

/// <summary>
/// The reservation projection port — the read side, and the single most guarded dependency in
/// this service. ARCHITECTURE.md "Defence in depth on the owner-facing path": "the reservation
/// projection port is injected <em>only</em> into that one interactor
/// [<c>ViewGiftListInteractor</c>], and the architecture tests assert no other type in the
/// Gateway references it" — <c>Gateway.UnitTests/Architecture/ReservationProjectionPortIsolationTests</c>
/// is that test. For an owner viewer that interactor never calls this at all: the field is
/// omitted, never fetched-then-hidden.
/// </summary>
/// <remarks>
/// Read-only by design. Writing the projection is a different port
/// (<c>Gateway.Application.Common.IReservationProjectionWriter</c>), so the pass-through
/// interactor that records a <c>GiftReservedV1</c> holds no handle that could read reservation
/// state back out — the isolation rule above is then exactly one type, not an allowlist of two.
/// The port lives beside the domain it serves (CONVENTIONS.md "Folder structure").
/// </remarks>
public interface IReservationProjectionRepository
{
    /// <summary>Every reserved item on one list. An empty list is the answer for a list nobody has reserved anything on — and for a list this projection has never heard of, which is the same fact here.</summary>
    Task<IReadOnlyList<ReservationProjection>> FindByListAsync(Guid listId, CancellationToken cancellationToken);
}
