using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.RecordGiftListRenamed;

/// <summary>
/// Pure pass-through — see <c>RecordGiftListCreatedInteractor</c>'s own doc comment for why the
/// last-write-wins guard belongs entirely behind <see cref="IGiftListProjectionRepository"/>.
/// </summary>
internal sealed class RecordGiftListRenamedInteractor(IGiftListProjectionRepository giftLists)
    : IRecordGiftListRenamed
{
    public async Task<Result<RecordGiftListRenamedResponse>> Handle(
        RecordGiftListRenamedRequest request, CancellationToken cancellationToken)
    {
        await giftLists.ApplyListRenamedAsync(request, cancellationToken);
        return new RecordGiftListRenamedResponse();
    }
}
