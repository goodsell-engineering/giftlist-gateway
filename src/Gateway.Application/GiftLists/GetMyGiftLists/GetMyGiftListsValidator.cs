using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.GetMyGiftLists;

internal sealed class GetMyGiftListsValidator : IValidator<GetMyGiftListsRequest>
{
    public Result Validate(GetMyGiftListsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.RequesterId == Guid.Empty ? GiftListErrors.InvalidId : Result.Success();
    }
}
