using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftItemDescriptionChanged;

/// <summary>Structural validation only — see <c>RecordGiftListCreatedValidator</c>'s own doc comment. No length rule: GiftLists already enforced it before publishing.</summary>
internal sealed class RecordGiftItemDescriptionChangedValidator : IValidator<RecordGiftItemDescriptionChangedRequest>
{
    public Result Validate(RecordGiftItemDescriptionChangedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ListId == Guid.Empty || request.ItemId == Guid.Empty)
        {
            return GiftListErrors.InvalidId;
        }

        return Result.Success();
    }
}
