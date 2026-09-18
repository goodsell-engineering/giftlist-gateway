using Gateway.Application.Common;
using HotChocolate.Subscriptions;

namespace Gateway.Infrastructure.Reservations.GraphQL;

/// <summary>
/// <see cref="IReservationChangeNotifier"/> over HotChocolate's in-memory topic sender
/// (ARCHITECTURE.md "Realtime updates" — "when the Gateway's event consumer handles
/// <c>GiftReserved</c> and updates <c>reservationProjection</c>, it pushes to HotChocolate's topic
/// event sender"). In-memory topics mean one Gateway instance; a multi-instance deployment would
/// swap this provider for a broker-backed one and nothing else changes, which is the point of the
/// port. Called from inside a Rebus handler's scope: <see cref="ITopicEventSender"/> is a
/// singleton, so it resolves there as it would anywhere.
/// </summary>
internal sealed class ReservationChangeNotifier(ITopicEventSender topics) : IReservationChangeNotifier
{
    public async Task NotifyReservationsChangedAsync(Guid listId, CancellationToken cancellationToken) =>
        await topics.SendAsync(ReservationTopics.ForList(listId), new ReservationsChanged(listId), cancellationToken);
}
