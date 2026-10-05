using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.RecordGiftItemDescriptionChanged;

/// <summary>
/// Pure pass-through — see <c>RecordGiftItemAddedInteractor</c>'s own doc comment. Ordering and
/// the stub-append/tombstone rules live in
/// <see cref="IGiftListProjectionRepository.ApplyItemDescriptionChangedAsync"/>.
/// </summary>
internal sealed class RecordGiftItemDescriptionChangedInteractor(IGiftListProjectionRepository giftLists)
    : IRecordGiftItemDescriptionChanged
{
    public async Task<Result<RecordGiftItemDescriptionChangedResponse>> Handle(
        RecordGiftItemDescriptionChangedRequest request, CancellationToken cancellationToken)
    {
        await giftLists.ApplyItemDescriptionChangedAsync(request, cancellationToken);
        return new RecordGiftItemDescriptionChangedResponse();
    }
}
