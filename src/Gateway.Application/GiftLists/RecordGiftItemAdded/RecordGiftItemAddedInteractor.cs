using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.RecordGiftItemAdded;

/// <summary>
/// Pure pass-through — see <c>RecordGiftListCreatedInteractor</c>'s own doc comment. The
/// item-keyed upsert (never an append) and the last-write-wins guard against a reordered
/// <c>GiftItemRemovedV1</c> for the same <c>ItemId</c> both live behind
/// <see cref="IGiftListProjectionRepository"/> — this interactor knows nothing about either.
/// </summary>
internal sealed class RecordGiftItemAddedInteractor(IGiftListProjectionRepository giftLists)
    : IRecordGiftItemAdded
{
    public async Task<Result<RecordGiftItemAddedResponse>> Handle(
        RecordGiftItemAddedRequest request, CancellationToken cancellationToken)
    {
        await giftLists.ApplyItemAddedAsync(request, cancellationToken);
        return new RecordGiftItemAddedResponse();
    }
}
