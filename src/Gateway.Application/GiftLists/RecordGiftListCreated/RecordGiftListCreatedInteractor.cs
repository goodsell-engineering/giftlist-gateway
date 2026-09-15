using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.RecordGiftListCreated;

/// <summary>
/// Assumes its request already passed <see cref="RecordGiftListCreatedValidator"/> — validation
/// is the composition root's decorator's job (CONVENTIONS.md "Use cases"), not this interactor's. All the
/// redelivery/reordering safety (GL-23) lives behind <see cref="IGiftListProjectionRepository"/>,
/// in Infrastructure — this interactor is a pure pass-through, same shape as every other
/// projection-writing use case in this folder.
/// </summary>
internal sealed class RecordGiftListCreatedInteractor(IGiftListProjectionRepository giftLists)
    : IRecordGiftListCreated
{
    public async Task<Result<RecordGiftListCreatedResponse>> Handle(
        RecordGiftListCreatedRequest request, CancellationToken cancellationToken)
    {
        await giftLists.ApplyListCreatedAsync(request, cancellationToken);
        return new RecordGiftListCreatedResponse();
    }
}
