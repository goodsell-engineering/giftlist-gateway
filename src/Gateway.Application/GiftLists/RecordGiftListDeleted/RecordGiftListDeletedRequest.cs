namespace Gateway.Application.GiftLists.RecordGiftListDeleted;

/// <summary>Translated from <c>GiftLists.Contracts.GiftLists.Events.GiftListDeletedV1</c> (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer").</summary>
public sealed record RecordGiftListDeletedRequest(Guid ListId, DateTimeOffset DeletedAt);
