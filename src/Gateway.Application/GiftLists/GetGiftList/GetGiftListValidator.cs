using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.GetGiftList;

internal sealed class GetGiftListValidator : IValidator<GetGiftListRequest>
{
    public Result Validate(GetGiftListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ListId == Guid.Empty || request.RequesterId == Guid.Empty)
        {
            return GiftListErrors.InvalidId;
        }

        return Result.Success();
    }
}
