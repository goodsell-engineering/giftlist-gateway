namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// Who is looking at a gift list, and with what credential — the one input the visibility rule
/// in <see cref="ViewGiftListInteractor"/> decides on (ARCHITECTURE.md "Defence in depth on the
/// owner-facing path": <c>Owner</c> | <c>GuestWithToken</c> | <c>Anonymous</c>). A closed
/// hierarchy — the private constructor means these three nested records are the only cases that
/// can ever exist, so the interactor's switch is exhaustive by construction rather than by
/// convention, and a fourth kind of viewer cannot be added without also deciding what it sees.
/// </summary>
/// <remarks>
/// <para>
/// The credential and the address travel together because they are not separable: an owner
/// addresses a list by id and proves who they are with a JWT subject; a guest's token is both the
/// address and the whole credential (ARCHITECTURE.md "Auth &amp; sharing" — "the token *is* the
/// capability"); an anonymous viewer has neither, so there is nothing for them to address.
/// </para>
/// <para>
/// There is deliberately no <c>Owner</c>-holding-a-token case. GL-39 settled that the owner
/// self-view interstitial is a client-side decision made from data the SPA already holds, and the
/// anonymous view carries no <c>ownerId</c> to decide it from server-side; a viewer presenting a
/// share token is a guest, whoever they also happen to be logged in as (ARCHITECTURE.md "Owner
/// shouldn't see what's reserved" — the owner is necessarily a bearer).
/// </para>
/// </remarks>
public abstract record ViewerContext
{
    private ViewerContext()
    {
    }

    /// <summary>
    /// A logged-in caller asking for one of their own lists by id. <paramref name="RequesterId"/>
    /// is the JWT's <c>sub</c>, never a client-supplied argument; whether it matches the list's
    /// owner is the interactor's decision, not the caller's claim.
    /// </summary>
    public sealed record Owner(Guid ListId, Guid RequesterId) : ViewerContext;

    /// <summary>A caller presenting a share token and nothing else — logged in or not is irrelevant.</summary>
    public sealed record GuestWithToken(string ShareToken) : ViewerContext;

    /// <summary>A caller with no JWT and no token. Sees nothing; exists so that answer is given here, once, rather than assumed by every transport.</summary>
    public sealed record Anonymous : ViewerContext;
}
