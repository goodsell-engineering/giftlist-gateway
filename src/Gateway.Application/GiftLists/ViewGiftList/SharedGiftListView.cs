namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// What an anonymous guest holding a share link may see of a gift list, and the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate type, not <see cref="GiftListProjection"/> with fields hidden at the resolver.</b>
/// The projection carries <c>OwnerId</c> and <c>ShareToken</c>; this type has no field for either
/// and so cannot be made to leak one by a later refactor of a resolver. ARCHITECTURE.md "Defence
/// in depth on the owner-facing path" states the reason directly — an <c>if (isOwner)
/// hideField()</c> is one refactor from leaking — and the same argument applies verbatim to an
/// anonymous view: omit by construction, never fetch-then-hide at the edge.
/// </para>
/// <para>
/// <b>Why <c>ownerId</c> specifically.</b> To an unauthenticated caller an owner GUID is a stable
/// cross-list correlation handle: two share links carrying the same GUID identify one person
/// across lists nobody said were related. That is the class of leak ARCHITECTURE.md "Nobody can
/// see *who* reserved" forbids, reached by a different road (GL-32).
/// </para>
/// <para>
/// <b>Why the token is not echoed back.</b> It is a bearer capability. A response that repeats it
/// hands it to whatever logs, caches or forwards the response, for no gain: the caller already
/// had it, since it is what they asked with.
/// </para>
/// <para>
/// <b>Why <c>ExpiresAt</c> is here and is not a filter.</b> Ryan's decision, 2026-09-16, recorded
/// on GL-32 and in ARCHITECTURE.md "Auth &amp; sharing": an expired list stays visible through the
/// share link, read-only. Expiry gates <em>reserving</em>, not <em>viewing</em> — which is also
/// the stated reason a Mongo TTL index is rejected for expiry ("we want an expired list to become
/// read-only but still visible"). So the interactor applies no expiry predicate, and the client
/// (GL-33/GL-42) renders the expired banner and disables reserve from this field. Do not re-open
/// this by adding a filter.
/// </para>
/// <para>
/// <b>Why the items are <see cref="SharedGiftItemView"/> (GL-38).</b> Until reservation data
/// existed in this service the items reused <see cref="GiftItemProjection"/>; now the guest's
/// item carries <c>reserved</c> and the owner's must not, so they are two types — see
/// <see cref="SharedGiftItemView"/> for the whole of what that field may ever be.
/// </para>
/// <para>
/// <c>ListId</c> is included because GL-42's reserve action addresses the list and item by id; it
/// is not a correlation handle to a person, and the caller is holding a link to this very list.
/// <c>CreatedAt</c> is omitted because nothing on the shared page renders it — this type carries
/// what the guest view needs and stops.
/// </para>
/// </remarks>
public sealed record SharedGiftListView(
    Guid ListId,
    string Name,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<SharedGiftItemView> Items);
