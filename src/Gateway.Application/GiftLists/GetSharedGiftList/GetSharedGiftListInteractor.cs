using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists.GetSharedGiftList;

/// <summary>
/// The share-link read (GL-32): one list, found by the token the caller presented, and nothing
/// else. There is no owner id in the request because there is no caller identity on this path —
/// the token is the whole credential (ARCHITECTURE.md "Auth &amp; sharing"), so scope comes from
/// the lookup itself rather than from a check after it. Contrast
/// <see cref="GetGiftList.GetGiftListInteractor"/>, which finds by id and must then prove
/// ownership; there is no equivalent second check to forget here, because a token that does not
/// resolve returns nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// The narrowing from <see cref="GiftListProjection"/> to <see cref="SharedGiftListView"/> happens
/// here, once, in the ring that owns the rule — not in the resolver. Everything outside this
/// method sees only the narrow type, which has no <c>OwnerId</c>/<c>ShareToken</c> field to
/// expose; see <see cref="SharedGiftListView"/> for why that is a type and not a resolver
/// condition.
/// </para>
/// <para>
/// No expiry predicate, deliberately — an expired list stays visible through the share link,
/// read-only (ARCHITECTURE.md "Auth &amp; sharing", amended 2026-09-16). Adding a filter here
/// would re-open a decision that is closed; see <see cref="SharedGiftListView"/>.
/// </para>
/// <para>
/// A token that is well-formed but resolves to nothing — unknown, or a list that has since been
/// deleted — is <see cref="GiftListErrors.NotFound"/>, the same fact and the same code
/// <c>giftList(id)</c> returns for a list that is not there (CONVENTIONS.md "Errors": a code names
/// a semantic, not where it was raised). That is deliberately distinguishable from the
/// <see cref="GiftListErrors.InvalidShareToken"/> a malformed token gets, because the two are
/// different facts and telling them apart costs nothing: the token's shape is public — it is
/// visible in every share URL — so "that is not a share token" reveals nothing a caller did not
/// already know, while conflating the two would tell a client with a typo to go looking for a
/// deleted list. Neither answer helps anyone enumerate lists: at 21 base62 characters, guessing a
/// token is the security property the whole scheme rests on.
/// </para>
/// </remarks>
internal sealed class GetSharedGiftListInteractor(IGiftListProjectionRepository giftLists) : IGetSharedGiftList
{
    public async Task<Result<GetSharedGiftListResponse>> Handle(
        GetSharedGiftListRequest request, CancellationToken cancellationToken)
    {
        var list = await giftLists.FindByShareTokenAsync(request.ShareToken, cancellationToken);
        if (list is null)
        {
            return GiftListErrors.NotFound;
        }

        return new GetSharedGiftListResponse(
            new SharedGiftListView(list.ListId, list.Name, list.ExpiresAt, list.Items));
    }
}
