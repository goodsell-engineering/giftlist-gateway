using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.GetGiftList;

/// <summary>
/// The security boundary this whole read model exists behind (GL-23 review, Batch 12): a
/// non-owner must never see another owner's list through this query. Mirrors
/// <c>GiftLists.Application.GiftLists.RenameGiftList.RenameGiftListInteractor</c>'s own
/// not-found-then-ownership-check shape exactly (ARCHITECTURE.md "Auth & sharing") — existence is checked
/// first (mirrors it doesn't own -&gt; genuinely doesn't exist), <em>then</em> ownership, so a
/// non-owner gets the same <see cref="GiftListErrors.Forbidden"/> whether the list exists or not
/// only insofar as GiftLists' own write-side check already made that same trade-off; this does
/// not additionally try to hide existence behind <see cref="GiftListErrors.NotFound"/>.
/// </summary>
internal sealed class GetGiftListInteractor(IGiftListProjectionRepository giftLists) : IGetGiftList
{
    public async Task<Result<GetGiftListResponse>> Handle(
        GetGiftListRequest request, CancellationToken cancellationToken)
    {
        var list = await giftLists.FindByIdAsync(request.ListId, cancellationToken);
        if (list is null)
        {
            return GiftListErrors.NotFound;
        }

        // ARCHITECTURE.md "Auth & sharing": owner queries require JWT + ownership check.
        if (list.OwnerId != request.RequesterId)
        {
            return GiftListErrors.Forbidden;
        }

        return new GetGiftListResponse(list);
    }
}
