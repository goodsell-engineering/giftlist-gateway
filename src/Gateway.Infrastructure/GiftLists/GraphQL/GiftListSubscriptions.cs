using Gateway.Application.Common;
using Gateway.Application.GiftLists.ViewGiftList;
using Gateway.Infrastructure.Platform.Transport;
using Gateway.Infrastructure.Reservations.GraphQL;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace Gateway.Infrastructure.GiftLists.GraphQL;

/// <summary>
/// The Gateway's root GraphQL Subscription type (GL-38), with exactly one field:
/// <c>sharedGiftListChanged(token)</c>, the share-token-scoped realtime channel a guest keeps
/// open so an item flips to "reserved" without a reload (ARCHITECTURE.md "Realtime updates").
/// There is deliberately no owner-facing subscription — no <c>giftListChanged(id)</c>, nothing
/// that takes a JWT — and none may be added to this type without re-reading that section: an
/// owner-facing channel carrying reservation data is the thing it rules out by name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two methods, one rule.</b> <see cref="SubscribeToSharedGiftListChanged"/> runs once, when
/// the client subscribes: it hands <c>ViewGiftList</c> the same <see cref="ViewerContext.GuestWithToken"/>
/// the query does, and a token that fails the shape check or resolves to no list fails the
/// subscription there and then with the same <c>gateway.invalid_share_token</c>/<c>gateway.not_found</c>
/// the query would give. Only a token that resolved is ever attached to a topic — keyed on the
/// list id the interactor found, never on the token (<see cref="ReservationTopics"/>).
/// <see cref="SharedGiftListChanged"/> then runs once per push, and does the same thing again:
/// the same interactor, the same viewer, a fresh guest view. The pushed payload is therefore
/// exactly the query's response type — <c>reserved: boolean</c> per item and no correlatable
/// identifier — because it is computed by the same code (ARCHITECTURE.md "Defence in depth on the
/// owner-facing path": "the same interactor backs the subscription channel"). Nothing on the topic
/// reaches the client; <see cref="ReservationsChanged"/> is a signal naming only the list.
/// </para>
/// <para>
/// Recomputing the whole view per push, per subscriber, is the honest cost of "one rule, one
/// implementation": a cheaper patch-shaped payload built in a handler would be a second place
/// that decides what a guest may see. In-memory topics mean a single Gateway instance; that is
/// recorded as out of scope in ARCHITECTURE.md "Realtime updates".
/// </para>
/// </remarks>
public sealed class GiftListSubscriptions
{
    public async ValueTask<ISourceStream<ReservationsChanged>> SubscribeToSharedGiftListChanged(
        string token,
        [Service] IInteractor<ViewGiftListRequest, ViewGiftListResponse> viewGiftList,
        [Service] ITopicEventReceiver topics,
        CancellationToken cancellationToken)
    {
        var view = await ViewAsGuestAsync(token, viewGiftList, cancellationToken);
        return await topics.SubscribeAsync<ReservationsChanged>(ReservationTopics.ForList(view.ListId), cancellationToken);
    }

    [Subscribe(With = nameof(SubscribeToSharedGiftListChanged))]
    [GraphQLName("sharedGiftListChanged")]
    public Task<SharedGiftListView> SharedGiftListChanged(
        string token,
        [EventMessage] ReservationsChanged changed,
        [Service] IInteractor<ViewGiftListRequest, ViewGiftListResponse> viewGiftList,
        CancellationToken cancellationToken)
    {
        // The signal's list id is not trusted to pick the view: the token is, through the same
        // lookup the query does. The topic was chosen from that token at subscribe time, so the
        // two agree unless the list has since been deleted — in which case the interactor's
        // not_found is the right answer, and the signal could not have made it otherwise.
        _ = changed;
        return ViewAsGuestAsync(token, viewGiftList, cancellationToken);
    }

    private static async Task<SharedGiftListView> ViewAsGuestAsync(
        string token,
        IInteractor<ViewGiftListRequest, ViewGiftListResponse> viewGiftList,
        CancellationToken cancellationToken)
    {
        var request = new ViewGiftListRequest(new ViewerContext.GuestWithToken(token));
        var result = await viewGiftList.Handle(request, cancellationToken);

        return result.Match(
            onSuccess: response => response.View.ForGuestOrThrow(),
            onFailure: error => throw error.ToGraphQlException());
    }
}
