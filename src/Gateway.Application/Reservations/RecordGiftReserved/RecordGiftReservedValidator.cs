using BuildingBlocks.Results;
using Gateway.Application.Common;
using Gateway.Application.GiftLists;

namespace Gateway.Application.Reservations.RecordGiftReserved;

/// <summary>
/// Structural validation only — see <c>RecordGiftListCreatedValidator</c>'s own doc comment.
/// Reuses <see cref="GiftListErrors.InvalidId"/> rather than minting a second
/// <c>gateway.invalid_id</c>: a code names a semantic, not where it was raised (CONVENTIONS.md
/// "Errors"), and "a required identifier was missing" is the same semantic here.
/// </summary>
internal sealed class RecordGiftReservedValidator : IValidator<RecordGiftReservedRequest>
{
    public Result Validate(RecordGiftReservedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ListId == Guid.Empty || request.ItemId == Guid.Empty)
        {
            return GiftListErrors.InvalidId;
        }

        return Result.Success();
    }
}
