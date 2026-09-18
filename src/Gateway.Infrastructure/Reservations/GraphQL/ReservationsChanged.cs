namespace Gateway.Infrastructure.Reservations.GraphQL;

/// <summary>
/// The message on a list's reservation topic (<see cref="ReservationTopics"/>): "something about
/// this list's reservations changed". A signal, not a payload — it names the list and nothing
/// else, and never reaches a client. What a subscriber receives is the guest view recomputed by
/// <c>ViewGiftListInteractor</c> for their own token (<c>GiftLists.GraphQL.GiftListSubscriptions</c>),
/// so nothing about a reservation itself — not which item, not when — travels on the topic
/// (ARCHITECTURE.md "Realtime updates").
/// </summary>
public sealed record ReservationsChanged(Guid ListId);
