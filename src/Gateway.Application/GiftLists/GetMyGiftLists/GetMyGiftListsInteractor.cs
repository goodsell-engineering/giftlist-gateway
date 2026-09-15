using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.GetMyGiftLists;

/// <summary>
/// Owner-scoped by construction (ARCHITECTURE.md "Auth & sharing"): <see cref="GetMyGiftListsRequest.RequesterId"/>
/// is the only filter this query ever applies, so there is no code path here that could return
/// another owner's lists — unlike <c>GetGiftListInteractor</c>, there is no separate ownership
/// check to get wrong because the caller's own id is the query, not a value compared against one.
/// </summary>
internal sealed class GetMyGiftListsInteractor(IGiftListProjectionRepository giftLists) : IGetMyGiftLists
{
    public async Task<Result<GetMyGiftListsResponse>> Handle(
        GetMyGiftListsRequest request, CancellationToken cancellationToken)
    {
        var lists = await giftLists.FindByOwnerAsync(request.RequesterId, cancellationToken);
        return new GetMyGiftListsResponse(lists);
    }
}
