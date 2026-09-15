namespace Gateway.Application.GiftLists.RecordGiftListRenamed;

/// <summary>Translated from <c>GiftLists.Contracts.GiftLists.Events.GiftListRenamedV1</c> (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer").</summary>
public sealed record RecordGiftListRenamedRequest(Guid ListId, string Name, DateTimeOffset RenamedAt);
