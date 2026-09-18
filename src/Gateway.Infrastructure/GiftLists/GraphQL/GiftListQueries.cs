using Gateway.Application.Common;
using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.GetMyGiftLists;
using Gateway.Application.GiftLists.ViewGiftList;
using Gateway.Infrastructure.Platform.Security;
using Gateway.Infrastructure.Platform.Transport;
using HotChocolate;
using Microsoft.AspNetCore.Http;

namespace Gateway.Infrastructure.GiftLists.GraphQL;

/// <summary>
/// The Gateway's root GraphQL Query type (GL-23) — <c>myGiftLists</c> and <c>giftList(id)</c>,
/// both owner-scoped by JWT (ARCHITECTURE.md "Auth & sharing"), plus <c>sharedGiftList(token)</c>
/// (GL-32), which is not authenticated at all. Thin by design, the same way a Rebus handler or
/// <c>AuthGrpcService</c> is thin: read the caller's id off the token, call the one input port,
/// translate a failure into a <see cref="GraphQLException"/>. There is no business logic here.
/// <c>giftList(id)</c> and <c>sharedGiftList(token)</c> are the same use case seen by two
/// viewers: each hands <c>ViewGiftList</c> a <see cref="ViewerContext"/> and maps the one case of
/// <see cref="GiftListView"/> its schema type is (GL-38). Ownership enforcement and the
/// reservation-visibility rule both live in <c>ViewGiftListInteractor</c>, not here, so they are
/// exercised the same way whichever surface calls it — including the subscription in
/// <see cref="GiftListSubscriptions"/> (ARCHITECTURE.md "Defence in depth on the owner-facing
/// path": "one rule, one implementation").
///
/// Reads the caller's principal off a plain <see cref="HttpContext"/> resolver parameter —
/// HotChocolate.AspNetCore's own parameter binding recognises that type specifically and hands it
/// the real per-request context; <see cref="IHttpContextAccessor"/> was tried first and does not
/// work here (GL-23 review, found the hard way): HotChocolate schedules resolver execution off
/// the <c>AsyncLocal</c> flow ASP.NET Core's request pipeline populated, so the accessor sees no
/// context by the time a resolver runs, and every query looked unauthenticated regardless of the
/// token sent.
///
/// <c>/graphql</c> is mapped with no authorization policy (<c>MapGraphQL()</c> in
/// <c>GatewayGraphQlEndpointRouteBuilderExtensions</c>, with no <c>RequireAuthorization()</c>),
/// and <c>Program.cs</c>'s <c>UseAuthentication()</c> populates <c>HttpContext.User</c> without
/// rejecting anything. So the endpoint is anonymous and each resolver opts in by asking for the
/// caller's id: <see cref="GetMyGiftLists"/> and <see cref="GetGiftList"/> call
/// <see cref="HttpContextExtensions.RequireUserId"/>, and <see cref="GetSharedGiftList"/>
/// deliberately does not — <em>not asking</em> is the whole of what makes that query public, which
/// is also why it takes no <see cref="HttpContext"/> parameter at all rather than taking one and
/// ignoring it.
/// </summary>
public sealed class GiftListQueries
{
    /// <summary>
    /// Every list the caller owns. <see cref="GetMyGiftListsRequest.RequesterId"/> is always the
    /// caller's own id (never a client-supplied argument), so this query has no code path that
    /// could return another owner's lists.
    /// </summary>
    [GraphQLName("myGiftLists")]
    public async Task<IReadOnlyList<GiftListProjection>> GetMyGiftLists(
        HttpContext httpContext,
        [Service] IInteractor<GetMyGiftListsRequest, GetMyGiftListsResponse> getMyGiftLists,
        CancellationToken cancellationToken)
    {
        var requesterId = httpContext.RequireUserId();
        var request = new GetMyGiftListsRequest(requesterId);
        var result = await getMyGiftLists.Handle(request, cancellationToken);

        return result.Match(
            onSuccess: response => response.GiftLists,
            onFailure: error => throw error.ToGraphQlException());
    }

    /// <summary>
    /// One list by id, only if the caller owns it — <c>gateway.forbidden</c>
    /// (<c>ErrorKind.Forbidden</c>, GraphQL <c>extensions.code</c> <c>FORBIDDEN</c>) otherwise,
    /// never the list's data (GL-23 review, Batch 12: this is the security boundary the whole
    /// read model exists behind). Returns <see cref="GiftListProjection"/>, a type with no
    /// reservation field: the owner viewer's view is decided by <c>ViewGiftListInteractor</c>
    /// without the reservation projection ever being read (GL-38).
    /// </summary>
    [GraphQLName("giftList")]
    public async Task<GiftListProjection> GetGiftList(
        Guid id,
        HttpContext httpContext,
        [Service] IInteractor<ViewGiftListRequest, ViewGiftListResponse> viewGiftList,
        CancellationToken cancellationToken)
    {
        var requesterId = httpContext.RequireUserId();
        var request = new ViewGiftListRequest(new ViewerContext.Owner(id, requesterId));
        var result = await viewGiftList.Handle(request, cancellationToken);

        return result.Match(
            onSuccess: response => response.View.ForOwnerOrThrow(),
            onFailure: error => throw error.ToGraphQlException());
    }

    /// <summary>
    /// One list by its share token, for a caller who is not logged in — the token is the whole
    /// credential (ARCHITECTURE.md "Auth &amp; sharing"). Returns
    /// <see cref="SharedGiftListView"/>, never <c>GiftListProjection</c>: the anonymous view has
    /// no <c>ownerId</c> or <c>shareToken</c> field in the schema at all, and its items carry
    /// <c>reserved: boolean</c> and nothing else about a reservation — both properties of the
    /// type rather than of this method (see <see cref="SharedGiftListView"/> and
    /// <see cref="SharedGiftItemView"/> for why they are omitted by construction rather than
    /// hidden here).
    ///
    /// An expired list still resolves, read-only — decided 2026-09-16, recorded in
    /// ARCHITECTURE.md "Auth &amp; sharing"; <c>expiresAt</c> is on the response so the SPA can
    /// render the expired banner and disable reserve (GL-42).
    /// </summary>
    [GraphQLName("sharedGiftList")]
    public async Task<SharedGiftListView> GetSharedGiftList(
        string token,
        [Service] IInteractor<ViewGiftListRequest, ViewGiftListResponse> viewGiftList,
        CancellationToken cancellationToken)
    {
        var request = new ViewGiftListRequest(new ViewerContext.GuestWithToken(token));
        var result = await viewGiftList.Handle(request, cancellationToken);

        return result.Match(
            onSuccess: response => response.View.ForGuestOrThrow(),
            onFailure: error => throw error.ToGraphQlException());
    }
}
