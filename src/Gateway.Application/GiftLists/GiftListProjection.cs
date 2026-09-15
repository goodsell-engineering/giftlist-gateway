namespace Gateway.Application.GiftLists;

/// <summary>
/// The Gateway's own read model of a gift list (CONVENTIONS.md "Naming" — Noun + "Projection"), built
/// from GiftLists' integration events (GL-23) and served to the SPA over GraphQL
/// (<c>myGiftLists</c> / <c>giftList(id)</c>). Deliberately not the
/// <c>GiftLists.Domain.GiftLists.GiftList</c> aggregate — this is a different service's read of
/// the same facts, kept in its own database (<c>gateway</c>) and updated asynchronously, at
/// least-once and out of order, by Rebus handlers (CONVENTIONS.md "Messaging").
/// </summary>
public sealed record GiftListProjection(
    Guid ListId,
    Guid OwnerId,
    string Name,
    DateTimeOffset ExpiresAt,
    string ShareToken,
    DateTimeOffset CreatedAt,
    IReadOnlyList<GiftItemProjection> Items);
