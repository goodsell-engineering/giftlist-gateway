using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.Reservations.RecordGiftReserved;

/// <summary>
/// Save first, notify second (CONVENTIONS.md "Messaging" — the same ordering as
/// save-then-publish). The upsert itself lives behind <see cref="IReservationProjectionWriter"/>;
/// this interactor holds no port that could read reservation state, deliberately — see
/// <c>IReservationProjectionRepository</c>'s own doc comment.
/// </summary>
/// <remarks>
/// Notifies on every delivery, including a redelivery whose write was a no-op. The alternative —
/// notify only when the upsert changed something — would make one lost notification permanent:
/// with the projection already written, a redelivery could never re-raise it. A subscriber who
/// receives the same guest view twice re-renders the same state, which costs nothing; a
/// subscriber who never receives it at all is what ARCHITECTURE.md "Realtime updates" exists to
/// prevent. At-least-once on the way in, at-least-once on the way out.
/// </remarks>
internal sealed class RecordGiftReservedInteractor(
    IReservationProjectionWriter reservations,
    IReservationChangeNotifier notifier)
    : IRecordGiftReserved
{
    public async Task<Result<RecordGiftReservedResponse>> Handle(
        RecordGiftReservedRequest request, CancellationToken cancellationToken)
    {
        await reservations.ApplyGiftReservedAsync(request.ListId, request.ItemId, cancellationToken);
        await notifier.NotifyReservationsChangedAsync(request.ListId, cancellationToken);
        return new RecordGiftReservedResponse();
    }
}
