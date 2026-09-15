namespace Gateway.Application.GiftLists;

/// <summary>
/// One item on a projected gift list (CONVENTIONS.md "Naming" — Noun + "Projection"). Never carries a
/// reservation field — that state lives entirely in the Reservation service and this projection
/// has no way to see it (ARCHITECTURE.md "Reservation privacy").
/// </summary>
public sealed record GiftItemProjection(Guid ItemId, string Name, string? Description, string? Url);
