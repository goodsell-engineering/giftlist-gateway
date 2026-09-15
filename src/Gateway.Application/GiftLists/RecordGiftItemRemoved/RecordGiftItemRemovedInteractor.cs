using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.RecordGiftItemRemoved;

/// <summary>
/// Pure pass-through — see <c>RecordGiftItemAddedInteractor</c>'s own doc comment. This is the
/// half of the "add-then-remove reordered" hazard (GL-23 review comments, Batch 12) that leaves a
/// tombstone behind when the remove is processed before the add — see
/// <c>IGiftListProjectionRepository.ApplyItemRemovedAsync</c>'s own doc comment for why a removed
/// item never disappears from storage outright.
/// </summary>
internal sealed class RecordGiftItemRemovedInteractor(IGiftListProjectionRepository giftLists)
    : IRecordGiftItemRemoved
{
    public async Task<Result<RecordGiftItemRemovedResponse>> Handle(
        RecordGiftItemRemovedRequest request, CancellationToken cancellationToken)
    {
        await giftLists.ApplyItemRemovedAsync(request, cancellationToken);
        return new RecordGiftItemRemovedResponse();
    }
}
