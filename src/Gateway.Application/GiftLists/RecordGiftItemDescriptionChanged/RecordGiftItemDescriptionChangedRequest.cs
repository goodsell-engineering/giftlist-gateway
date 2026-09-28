namespace Gateway.Application.GiftLists.RecordGiftItemDescriptionChanged;

/// <summary>Translated from <c>GiftLists.Contracts.GiftLists.Events.GiftItemDescriptionChangedV1</c> (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer"). A null <see cref="Description"/> means cleared.</summary>
public sealed record RecordGiftItemDescriptionChangedRequest(Guid ListId, Guid ItemId, string? Description, DateTimeOffset ChangedAt);
