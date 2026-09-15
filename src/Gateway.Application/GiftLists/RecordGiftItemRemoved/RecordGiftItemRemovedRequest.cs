namespace Gateway.Application.GiftLists.RecordGiftItemRemoved;

/// <summary>Translated from <c>GiftLists.Contracts.GiftLists.Events.GiftItemRemovedV1</c> (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer").</summary>
public sealed record RecordGiftItemRemovedRequest(Guid ListId, Guid ItemId, DateTimeOffset RemovedAt);
