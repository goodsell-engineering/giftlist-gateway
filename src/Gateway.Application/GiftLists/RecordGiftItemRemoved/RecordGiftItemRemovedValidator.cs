using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftItemRemoved;

/// <summary>Structural validation only — see <c>RecordGiftListCreatedValidator</c>'s own doc comment.</summary>
internal sealed class RecordGiftItemRemovedValidator : IValidator<RecordGiftItemRemovedRequest>
{
    public Result Validate(RecordGiftItemRemovedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ListId == Guid.Empty || request.ItemId == Guid.Empty)
        {
            return GiftListErrors.InvalidId;
        }

        return Result.Success();
    }
}
