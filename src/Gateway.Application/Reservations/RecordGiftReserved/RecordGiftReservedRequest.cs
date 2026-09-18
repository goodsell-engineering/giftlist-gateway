namespace Gateway.Application.Reservations.RecordGiftReserved;

/// <summary>
/// Translated from <c>Reservations.Contracts.Reservations.Events.GiftReservedV1</c> at the
/// boundary (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer"). Two
/// fields, where the event has three: its <c>ReservedAt</c> is dropped by the translating handler
/// and never reaches this ring — a timestamp is a weak signal that two reservations belong to one
/// person, and nothing in the Gateway has a use for it (ARCHITECTURE.md "Nobody can see *who*
/// reserved"; <c>ReservationProjection</c>'s own doc comment for why not even the projection
/// keeps it).
/// </summary>
public sealed record RecordGiftReservedRequest(Guid ListId, Guid ItemId);
