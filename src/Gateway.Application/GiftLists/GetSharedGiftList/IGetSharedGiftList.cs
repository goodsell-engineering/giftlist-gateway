using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.GetSharedGiftList;

/// <summary>
/// The named input port behind GraphQL's unauthenticated <c>sharedGiftList(token)</c> query
/// (CONVENTIONS.md "Naming"). The token is the whole credential — there is no JWT on this path
/// (ARCHITECTURE.md "Auth &amp; sharing": "the token *is* the capability").
/// </summary>
/// <remarks>
/// <para>
/// <b>How this relates to GL-38's <c>ViewGiftList(viewer)</c>.</b> ARCHITECTURE.md "Defence in
/// depth on the owner-facing path" puts the reservation-visibility rule in exactly one
/// interactor, <c>ViewGiftList</c>, taking a <c>ViewerContext</c>
/// (<c>Owner</c> | <c>GuestWithToken</c> | <c>Anonymous</c>). This use case is the
/// guest-with-token read as it exists <em>before</em> any reservation data exists in this system
/// at all (Reservations' projection is GL-35/36/37) — so there is deliberately no
/// <c>ViewerContext</c>, no reservation port and no seam for one here: an abstraction with one
/// implemented branch and nothing to decide would be a guess at GL-38's shape, not a head start
/// on it. What GL-38 inherits is the ordinary thing: a named input port in its own folder, with
/// its own request, response and validator, which it can absorb or replace outright. The one
/// property GL-38 must keep is the one below — the anonymous response type physically cannot
/// carry <c>ownerId</c> or the token.
/// </para>
/// </remarks>
public interface IGetSharedGiftList : IInteractor<GetSharedGiftListRequest, GetSharedGiftListResponse>;
