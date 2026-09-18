namespace Gateway.Application.GiftLists;

/// <summary>
/// One item on a projected gift list (CONVENTIONS.md "Naming" — Noun + "Projection"), as the
/// list's owner sees it. Never carries a reservation field, and must not grow one: this is the
/// type the owner-facing surfaces return, and the owner path is kept clean by this type having
/// nowhere to put reservation state rather than by a resolver remembering to leave it out
/// (ARCHITECTURE.md "Defence in depth on the owner-facing path"). The guest's item is a separate
/// type, <c>ViewGiftList.SharedGiftItemView</c>, which carries <c>reserved: boolean</c> and only
/// that (ARCHITECTURE.md "Nobody can see *who* reserved").
/// </summary>
public sealed record GiftItemProjection(Guid ItemId, string Name, string? Description, string? Url);
