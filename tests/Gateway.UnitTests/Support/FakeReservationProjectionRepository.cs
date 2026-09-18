using Gateway.Application.Reservations;

namespace Gateway.UnitTests.Support;

/// <summary>
/// In-memory stand-in for the reservation projection's read port. Records every call, because
/// the property under test for an owner viewer is not what this returns but that it is
/// <em>never asked</em> (ARCHITECTURE.md "Defence in depth on the owner-facing path" — omitted,
/// never fetched-then-hidden).
/// </summary>
internal sealed class FakeReservationProjectionRepository : IReservationProjectionRepository
{
    private readonly List<ReservationProjection> _reservations = [];

    public List<Guid> ListsAskedFor { get; } = [];

    public void Seed(Guid listId, Guid itemId) => _reservations.Add(new ReservationProjection(listId, itemId));

    public Task<IReadOnlyList<ReservationProjection>> FindByListAsync(Guid listId, CancellationToken cancellationToken)
    {
        ListsAskedFor.Add(listId);
        return Task.FromResult<IReadOnlyList<ReservationProjection>>(_reservations.Where(r => r.ListId == listId).ToList());
    }
}
