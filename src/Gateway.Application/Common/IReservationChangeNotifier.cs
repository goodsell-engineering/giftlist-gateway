namespace Gateway.Application.Common;

/// <summary>
/// Tells whoever is watching a list's shared view that its reservation state has changed
/// (ARCHITECTURE.md "Realtime updates": the Gateway's event consumer "pushes to HotChocolate's
/// topic event sender; subscribed clients get the patch"). Called by the interactor after the
/// projection write — save first, notify second, the same ordering as save-then-publish
/// (CONVENTIONS.md "Messaging").
/// </summary>
/// <remarks>
/// <para>
/// Carries the list id and nothing else. The notification is a signal, not a payload: what a
/// subscriber then receives is computed by the same <c>ViewGiftList</c> interactor that serves
/// the query, for the same guest viewer, so the pushed view carries <c>reserved: boolean</c> and
/// no correlatable identifier (ARCHITECTURE.md "Realtime updates" — "same rule as the privacy
/// section"). Nothing about the reservation itself travels through this port.
/// </para>
/// <para>
/// Lives in <c>Common/</c> for the reason <see cref="IReservationProjectionWriter"/>'s own doc
/// comment gives: it is an injected collaborator port, not a use case and not a repository.
/// </para>
/// </remarks>
public interface IReservationChangeNotifier
{
    Task NotifyReservationsChangedAsync(Guid listId, CancellationToken cancellationToken);
}
