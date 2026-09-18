namespace Gateway.Infrastructure.Reservations.GraphQL;

/// <summary>
/// Names the HotChocolate topic a list's reservation changes are published on — one place, so
/// the sender (<see cref="ReservationChangeNotifier"/>) and the receiver
/// (<c>GiftLists.GraphQL.GiftListSubscriptions</c>) cannot drift apart on a string.
/// Keyed on the list id, an internal handle: the share token a subscriber presents is resolved to
/// the list id by <c>ViewGiftListInteractor</c> at subscribe time, so the token itself is never
/// part of a topic name that logging or a future broker-backed provider might record.
/// </summary>
internal static class ReservationTopics
{
    public static string ForList(Guid listId) => $"giftList:{listId:N}:reservations";
}
