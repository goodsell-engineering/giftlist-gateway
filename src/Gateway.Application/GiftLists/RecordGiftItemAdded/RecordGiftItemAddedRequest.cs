namespace Gateway.Application.GiftLists.RecordGiftItemAdded;

/// <summary>Translated from <c>GiftLists.Contracts.GiftLists.Events.GiftItemAddedV1</c> (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer").</summary>
public sealed record RecordGiftItemAddedRequest(
    Guid ListId,
    Guid ItemId,
    string Name,
    string? Description,
    string? Url,
    DateTimeOffset AddedAt);
