using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftListRenamed;

/// <summary>Structural validation only — see <c>RecordGiftListCreatedValidator</c>'s own doc comment.</summary>
internal sealed class RecordGiftListRenamedValidator : IValidator<RecordGiftListRenamedRequest>
{
    public Result Validate(RecordGiftListRenamedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ListId == Guid.Empty ? GiftListErrors.InvalidId : Result.Success();
    }
}
