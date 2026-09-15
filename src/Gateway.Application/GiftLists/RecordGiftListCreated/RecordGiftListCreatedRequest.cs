namespace Gateway.Application.GiftLists.RecordGiftListCreated;

/// <summary>
/// The Infrastructure-translated shape of <c>GiftLists.Contracts.GiftLists.Events.GiftListCreatedV1</c>
/// (CONVENTIONS.md "Project reference graph" — Application never sees a Contracts type directly; a Rebus handler in
/// Infrastructure does that translation). Delivery is at-least-once and not guaranteed in order
/// (GL-64's redelivery-after-a-lost-race hazard) — this request carries every field the
/// projection needs to be rebuilt from scratch, never a delta.
/// </summary>
public sealed record RecordGiftListCreatedRequest(
    Guid ListId,
    Guid OwnerId,
    string Name,
    DateTimeOffset ExpiresAt,
    string ShareToken,
    DateTimeOffset CreatedAt);
