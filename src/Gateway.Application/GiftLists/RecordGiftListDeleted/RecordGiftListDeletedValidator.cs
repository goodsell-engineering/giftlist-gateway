using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftListDeleted;

/// <summary>Structural validation only — see <c>RecordGiftListCreatedValidator</c>'s own doc comment.</summary>
internal sealed class RecordGiftListDeletedValidator : IValidator<RecordGiftListDeletedRequest>
{
    public Result Validate(RecordGiftListDeletedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ListId == Guid.Empty ? GiftListErrors.InvalidId : Result.Success();
    }
}
