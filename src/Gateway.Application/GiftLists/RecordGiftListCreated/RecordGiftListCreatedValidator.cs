using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftListCreated;

/// <summary>
/// Structural validation only — "is this a well-formed translation of the wire event", not a
/// business rule. GiftLists already validated the facts before publishing them; re-validating
/// business rules here would be the ACL boundary leaking a different service's invariants into
/// this one (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer").
/// </summary>
internal sealed class RecordGiftListCreatedValidator : IValidator<RecordGiftListCreatedRequest>
{
    public Result Validate(RecordGiftListCreatedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ListId == Guid.Empty || request.OwnerId == Guid.Empty)
        {
            return GiftListErrors.InvalidId;
        }

        return Result.Success();
    }
}
