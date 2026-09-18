namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// One item as a guest holding the share link sees it: the item itself, plus
/// <paramref name="Reserved"/> — a boolean, and the only reservation fact this system ever
/// surfaces to anyone (ARCHITECTURE.md "Nobody can see *who* reserved": "The guest view returns
/// <c>reserved: boolean</c> per item and nothing more").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a boolean and not a timestamp.</b> <c>GiftReservedV1</c> carries a <c>ReservedAt</c>;
/// this type has no field for it and none may be added. Two items reserved within the same
/// minute is exactly the kind of value that "lets a viewer infer <em>the same person</em> reserved
/// several items" — the leak the same section forbids by name. The timestamp stops at the
/// anti-corruption layer (<c>GiftReservedV1Handler</c> translates the three-field event into a
/// two-field request) and is never stored in the Gateway at all, so there is nothing here to
/// derive one from either. The same goes for anything that could correlate: no reservation id,
/// no release secret (<c>releaseSecret</c> travels only in the reserving browser's own reply and
/// its <c>localStorage</c>, ARCHITECTURE.md "Data model"), no per-session grouping of any kind.
/// </para>
/// <para>
/// <b>Why a separate type from <see cref="GiftItemProjection"/>.</b> That type is what the
/// owner's read model carries, and it must never grow a reservation field — the owner-facing
/// path is kept clean by that type having nowhere to put one (ARCHITECTURE.md "Defence in depth
/// on the owner-facing path"). So the guest's item is its own type with the one extra field,
/// rather than a nullable <c>Reserved</c> on the shared type that an owner resolver would then
/// have to remember to leave null.
/// </para>
/// </remarks>
public sealed record SharedGiftItemView(Guid ItemId, string Name, string? Description, string? Url, bool Reserved);
