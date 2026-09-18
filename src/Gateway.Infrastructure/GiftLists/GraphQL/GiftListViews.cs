using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.ViewGiftList;

namespace Gateway.Infrastructure.GiftLists.GraphQL;

/// <summary>
/// The resolver-side half of <see cref="GiftListView"/>'s union: each GraphQL surface asks for
/// the one case its schema type is, and gets an exception — never the other case's data — if
/// <c>ViewGiftListInteractor</c> answered with something else. That would be a bug in the rule's
/// owner, not an expected outcome, so it is an exception rather than a <c>Result</c>
/// (CONVENTIONS.md "Errors"), and it fails the request rather than mapping across: an owner
/// resolver that could ever fall through to a guest view would be the <c>if (isOwner)
/// hideField()</c> shape ARCHITECTURE.md "Defence in depth on the owner-facing path" exists to
/// rule out, reached backwards.
/// </summary>
internal static class GiftListViews
{
    public static GiftListProjection ForOwnerOrThrow(this GiftListView view) => view switch
    {
        GiftListView.ForOwner owner => owner.GiftList,
        _ => throw Mismatch(view, nameof(GiftListView.ForOwner)),
    };

    public static SharedGiftListView ForGuestOrThrow(this GiftListView view) => view switch
    {
        GiftListView.ForGuest guest => guest.GiftList,
        _ => throw Mismatch(view, nameof(GiftListView.ForGuest)),
    };

    private static InvalidOperationException Mismatch(GiftListView view, string expected) => new(
        $"ViewGiftList answered a {view.GetType().Name} where this surface can only serve a {expected}. " +
        "The viewer context handed over and the view returned no longer agree — a bug in " +
        "ViewGiftListInteractor's visibility rule, not something a resolver may paper over.");
}
