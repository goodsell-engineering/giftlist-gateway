namespace Gateway.Application.Reservations;

/// <summary>
/// The Gateway's read of one fact from Reservations: item <paramref name="ItemId"/> on list
/// <paramref name="ListId"/> is reserved (CONVENTIONS.md "Naming" — Noun + "Projection"). Two ids
/// and nothing else, on purpose: this is the entire reservation state this service holds, and
/// its poverty is the guarantee (ARCHITECTURE.md "Nobody can see *who* reserved" — enforced by
/// the absence of data). No <c>ReservedAt</c>, though <c>GiftReservedV1</c> carries one: a
/// timestamp is a weak correlation signal between two reservations, and nothing here needs it —
/// there is no ordering to resolve between a reserve and a release while no release event exists.
/// If one arrives later, its ordering key is designed with that contract, not stored ahead of it.
/// </summary>
public sealed record ReservationProjection(Guid ListId, Guid ItemId);
