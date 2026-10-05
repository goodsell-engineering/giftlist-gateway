using Gateway.Application.Reservations;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// GL-137 (TC-T2-21): a singleton counter of every call to
/// <see cref="IReservationProjectionRepository.FindByListAsync"/>, shared across every DI scope —
/// the owner grpc-web call and the <c>GiftItemDescriptionChangedV1</c> Rebus handler each get
/// their own scope, so the count could not otherwise be read back from a single one of them. See
/// <see cref="CountingReservationProjectionRepository"/>'s own doc comment for what it counts and
/// why.
/// </summary>
public sealed class ReservationProjectionCallCounter
{
    private int _findByListCallCount;

    public int FindByListCallCount => _findByListCallCount;

    public void RecordFindByListCall() => Interlocked.Increment(ref _findByListCallCount);
}

/// <summary>
/// GL-137 (TC-T2-21): counts every call to the one read method
/// <see cref="IReservationProjectionRepository"/> exposes, onto a shared
/// <see cref="ReservationProjectionCallCounter"/>, so a test can prove the owner-edit flow
/// (<c>ChangeGiftItemDescription</c> → <c>GiftItemDescriptionChangedV1</c> →
/// <c>RecordGiftItemDescriptionChanged</c>) reads no reservation data at all — not merely that it
/// returns the right answer despite reading some. Registered via <c>services.Decorate</c>
/// (Scrutor) over the production <c>ReservationProjectionRepository</c> in
/// <see cref="GatewayFixture"/>'s <c>ConfigureServices</c>, so every call the real DI graph makes
/// — including the one call <c>ViewGiftListInteractor</c> itself makes, which every other test in
/// the suite relies on continuing to work — passes through here first.
/// </summary>
internal sealed class CountingReservationProjectionRepository(
    IReservationProjectionRepository inner, ReservationProjectionCallCounter counter)
    : IReservationProjectionRepository
{
    public Task<IReadOnlyList<ReservationProjection>> FindByListAsync(Guid listId, CancellationToken cancellationToken)
    {
        counter.RecordFindByListCall();
        return inner.FindByListAsync(listId, cancellationToken);
    }
}
