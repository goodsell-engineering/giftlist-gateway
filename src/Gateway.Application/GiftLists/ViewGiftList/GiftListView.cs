namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// What a viewer gets back: one of two types with different fields, never one type with fields
/// blanked out. A closed hierarchy (private constructor), mirroring <see cref="ViewerContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The two cases carry different data <em>by type</em>, which is the whole point. The owner view
/// is the <see cref="GiftListProjection"/> as it is — <c>ownerId</c>, <c>shareToken</c>,
/// <c>createdAt</c>, items with no reservation state, because the type has no field for any —
/// while the guest view (<see cref="SharedGiftListView"/>) has no field for <c>ownerId</c> or the
/// token, and its items carry <c>reserved</c>. Neither can be made to leak the other's fields by
/// a later edit of a resolver, because there is no field to un-hide (ARCHITECTURE.md "Defence in
/// depth on the owner-facing path": an <c>if (isOwner) hideField()</c> is one refactor from
/// leaking).
/// </para>
/// <para>
/// One response type holding a union, rather than two use cases returning two types, because the
/// rule that decides which case a viewer gets is meant to live in exactly one interactor
/// (same section: "one rule, one implementation, or the guarantee leaks through whichever path
/// forgot it"). A resolver that asks for the wrong case gets an exception, not the other case's
/// data — see <c>GiftListViews</c> in Infrastructure.
/// </para>
/// </remarks>
public abstract record GiftListView
{
    private GiftListView()
    {
    }

    /// <summary>The owner's read of their own list. Carries nothing about reservations because <see cref="GiftListProjection"/> has nowhere to put it.</summary>
    public sealed record ForOwner(GiftListProjection GiftList) : GiftListView;

    /// <summary>A guest's read through the share link — <c>reserved: boolean</c> per item and nothing more (ARCHITECTURE.md "Nobody can see *who* reserved").</summary>
    public sealed record ForGuest(SharedGiftListView GiftList) : GiftListView;
}
