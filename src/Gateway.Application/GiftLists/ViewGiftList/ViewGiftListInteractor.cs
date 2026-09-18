using BuildingBlocks.Results;
using Gateway.Application.Reservations;

namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// The visibility rule, in the one place it lives (ARCHITECTURE.md "Defence in depth on the
/// owner-facing path"): given a <see cref="ViewerContext"/>, decide which view of the list that
/// viewer gets — and, for an owner, decide it <em>without ever calling</em>
/// <see cref="IReservationProjectionRepository"/>. The field is omitted, never fetched-then-hidden;
/// <c>ReservationProjectionPortIsolationTests</c> pins this as the only type that may hold that
/// port, and the unit tests pin that the owner branch never touches it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner.</b> Finds by id, then proves ownership. Mirrors
/// <c>GiftLists.Application.GiftLists.RenameGiftList.RenameGiftListInteractor</c>'s own
/// not-found-then-ownership-check shape (ARCHITECTURE.md "Auth &amp; sharing") — existence first,
/// then ownership, so a non-owner gets <see cref="GiftListErrors.Forbidden"/> for a list that
/// exists, exactly as GiftLists' own write side answers; this does not additionally try to hide
/// existence behind <see cref="GiftListErrors.NotFound"/> (GL-23 review, Batch 12). The response
/// is the <see cref="GiftListProjection"/> as projected, which has no reservation field to fill.
/// </para>
/// <para>
/// <b>Guest with token.</b> Finds by token — the token is the whole credential, so scope comes
/// from the lookup itself rather than from a check after it — then, and only then, asks the
/// reservation projection which of the list's items are reserved, and narrows to
/// <see cref="SharedGiftListView"/>: no <c>OwnerId</c>, no token, and <c>reserved: boolean</c>
/// per item (ARCHITECTURE.md "Nobody can see *who* reserved"). No expiry predicate, deliberately:
/// an expired list stays visible through the share link, read-only (ARCHITECTURE.md "Auth &amp;
/// sharing", amended 2026-09-16). A token that is well-formed but resolves to nothing — unknown,
/// or a list that has since been deleted — is <see cref="GiftListErrors.NotFound"/>, the same
/// fact and the same code the owner path returns (CONVENTIONS.md "Errors": a code names a
/// semantic, not where it was raised), and deliberately distinguishable from the
/// <see cref="GiftListErrors.InvalidShareToken"/> a malformed token gets from the validator:
/// the token's shape is public (it is in every share URL), so "that is not a share token" reveals
/// nothing, while conflating the two would tell a client with a typo to go looking for a deleted
/// list.
/// </para>
/// <para>
/// <b>Anonymous.</b> Sees nothing — <see cref="GiftListErrors.Unauthenticated"/>. Today every
/// GraphQL surface settles this at the transport before the interactor is reached (an owner
/// resolver requires a subject off the JWT; a guest resolver requires a token argument), so the
/// branch is reached from no production caller yet. It is here so that the answer for "no
/// credential at all" is stated by the rule's owner, once, rather than left for the next surface
/// to assume.
/// </para>
/// </remarks>
internal sealed class ViewGiftListInteractor(
    IGiftListProjectionRepository giftLists,
    IReservationProjectionRepository reservations)
    : IViewGiftList
{
    public Task<Result<ViewGiftListResponse>> Handle(ViewGiftListRequest request, CancellationToken cancellationToken) =>
        request.Viewer switch
        {
            ViewerContext.Owner owner => ViewAsOwnerAsync(owner, cancellationToken),
            ViewerContext.GuestWithToken guest => ViewAsGuestAsync(guest, cancellationToken),
            ViewerContext.Anonymous => Task.FromResult(Result<ViewGiftListResponse>.Failure(GiftListErrors.Unauthenticated)),
            _ => throw new InvalidOperationException(
                $"Unknown viewer context '{request.Viewer.GetType().Name}'. ViewerContext is a closed " +
                "hierarchy; a new case must be given a visibility decision here, not defaulted."),
        };

    private async Task<Result<ViewGiftListResponse>> ViewAsOwnerAsync(
        ViewerContext.Owner owner, CancellationToken cancellationToken)
    {
        var list = await giftLists.FindByIdAsync(owner.ListId, cancellationToken);
        if (list is null)
        {
            return GiftListErrors.NotFound;
        }

        // ARCHITECTURE.md "Auth & sharing": owner queries require JWT + ownership check.
        if (list.OwnerId != owner.RequesterId)
        {
            return GiftListErrors.Forbidden;
        }

        // No reservation lookup on this path, by construction — not skipped, not filtered:
        // never asked for (ARCHITECTURE.md "Defence in depth on the owner-facing path").
        return new ViewGiftListResponse(new GiftListView.ForOwner(list));
    }

    private async Task<Result<ViewGiftListResponse>> ViewAsGuestAsync(
        ViewerContext.GuestWithToken guest, CancellationToken cancellationToken)
    {
        var list = await giftLists.FindByShareTokenAsync(guest.ShareToken, cancellationToken);
        if (list is null)
        {
            return GiftListErrors.NotFound;
        }

        var reservedItemIds = (await reservations.FindByListAsync(list.ListId, cancellationToken))
            .Select(reservation => reservation.ItemId)
            .ToHashSet();

        var items = list.Items
            .Select(item => new SharedGiftItemView(
                item.ItemId, item.Name, item.Description, item.Url, Reserved: reservedItemIds.Contains(item.ItemId)))
            .ToList();

        return new ViewGiftListResponse(
            new GiftListView.ForGuest(new SharedGiftListView(list.ListId, list.Name, list.ExpiresAt, items)));
    }
}
