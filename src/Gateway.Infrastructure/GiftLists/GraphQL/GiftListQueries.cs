using Gateway.Application.Common;
using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.GetGiftList;
using Gateway.Application.GiftLists.GetMyGiftLists;
using Gateway.Infrastructure.Platform.Security;
using Gateway.Infrastructure.Platform.Transport;
using HotChocolate;
using Microsoft.AspNetCore.Http;

namespace Gateway.Infrastructure.GiftLists.GraphQL;

/// <summary>
/// The Gateway's root GraphQL Query type (GL-23) — <c>myGiftLists</c> and <c>giftList(id)</c>,
/// both owner-scoped by JWT (ARCHITECTURE.md "Auth & sharing"). Thin by design, the same way a Rebus handler or
/// <c>AuthGrpcService</c> is thin: read the caller's id off the token, call the one input port,
/// translate a failure into a <see cref="GraphQLException"/>. There is no business logic here —
/// ownership enforcement itself lives in <c>GetGiftListInteractor</c>, not in this resolver, so it
/// is exercised the same way whichever surface calls it.
///
/// Reads the caller's principal off a plain <see cref="HttpContext"/> resolver parameter —
/// HotChocolate.AspNetCore's own parameter binding recognises that type specifically and hands it
/// the real per-request context; <see cref="IHttpContextAccessor"/> was tried first and does not
/// work here (GL-23 review, found the hard way): HotChocolate schedules resolver execution off
/// the <c>AsyncLocal</c> flow ASP.NET Core's request pipeline populated, so the accessor sees no
/// context by the time a resolver runs, and every query looked unauthenticated regardless of the
/// token sent.
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
    /// read model exists behind).
    /// </summary>
    [GraphQLName("giftList")]
    public async Task<GiftListProjection> GetGiftList(
        Guid id,
        HttpContext httpContext,
        [Service] IInteractor<GetGiftListRequest, GetGiftListResponse> getGiftList,
        CancellationToken cancellationToken)
    {
        var requesterId = httpContext.RequireUserId();
        var request = new GetGiftListRequest(id, requesterId);
        var result = await getGiftList.Handle(request, cancellationToken);

        return result.Match(
            onSuccess: response => response.GiftList,
            onFailure: error => throw error.ToGraphQlException());
    }
}
