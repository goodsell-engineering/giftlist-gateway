using Gateway.Application.GiftLists.RecordGiftItemAdded;
using Gateway.Application.GiftLists.RecordGiftItemRemoved;
using Gateway.Application.GiftLists.RecordGiftListCreated;
using Gateway.Application.GiftLists.RecordGiftListDeleted;
using Gateway.Application.GiftLists.RecordGiftListRenamed;

namespace Gateway.Application.GiftLists;

/// <summary>
/// Port lives beside the domain it serves (CONVENTIONS.md "Folder structure"), not in a shared Abstractions
/// bucket. One repository over the one projection this service has.
///
/// The five <c>Apply*</c> methods are the whole of GL-23's redelivery/reordering story
/// (Batch 12 review comments): each is expected to be an <em>idempotent, last-write-wins
/// upsert</em>, never a blind insert or append (CONVENTIONS.md "Messaging") —
/// <list type="bullet">
/// <item>
/// <see cref="ApplyListCreatedAsync"/>/<see cref="ApplyListRenamedAsync"/>/
/// <see cref="ApplyListDeletedAsync"/> are keyed on <c>ListId</c>, matching
/// <c>GiftListCreatedV1</c>/<c>RenamedV1</c>/<c>DeletedV1</c>'s own natural key.
/// </item>
/// <item>
/// <see cref="ApplyItemAddedAsync"/>/<see cref="ApplyItemRemovedAsync"/> are keyed on
/// <c>ItemId</c> <em>within</em> the list — the items array is item-keyed, never appended to,
/// specifically so a redelivered <c>GiftItemAddedV1</c> cannot duplicate the item.
/// </item>
/// <item>
/// Every one of the five compares the incoming event's own timestamp
/// (<c>CreatedAt</c>/<c>RenamedAt</c>/<c>DeletedAt</c>/<c>AddedAt</c>/<c>RemovedAt</c>) against
/// what is already stored and ignores anything older — idempotency alone only protects against
/// redelivery of the <em>same</em> event, not against two <em>different</em> events for the same
/// list/item arriving out of order (GL-64 made this a real hazard, not a hypothetical one: a
/// writer that loses GiftLists' own optimistic-concurrency race has its message redelivered and
/// its event published seconds later than the message that won). The sharpest case is
/// <see cref="ApplyItemAddedAsync"/> arriving after <see cref="ApplyItemRemovedAsync"/> for the
/// same <c>ItemId</c> — see that member's own doc comment for why it must not resurrect the item.
/// </item>
/// </list>
/// A per-aggregate version counter (<c>GiftList.Version</c>, GL-64) would be a strictly better
/// ordering key than a wall-clock timestamp, but it is deliberately not on the wire — putting it
/// there is a breaking contract change, out of scope here (GL-23 report flags this as a finding).
///
/// Each of the five <c>Apply*</c> methods retries its own compare-and-set internally, up to a
/// bounded attempt cap (GL-76) — see <see cref="GiftListProjectionApplyExhaustedException"/> for
/// what happens, and why, once that cap is exhausted.
/// </summary>
public interface IGiftListProjectionRepository
{
    Task<GiftListProjection?> FindByIdAsync(Guid listId, CancellationToken cancellationToken);

    /// <summary>Owner-scoped at the query itself (ARCHITECTURE.md "Auth & sharing") — the only filter <c>myGiftLists</c> ever applies.</summary>
    Task<IReadOnlyList<GiftListProjection>> FindByOwnerAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// GL-31: the read-model half of the share link — looks a projection up by its
    /// <c>shareToken</c> rather than its <c>ListId</c>/<c>OwnerId</c>. Same null-when-absent
    /// convention as <see cref="FindByIdAsync"/>, including for a stub row
    /// (<c>HasCreated == false</c>) or a soft-deleted one. Deliberately stops here: no GraphQL
    /// query, resolver or interactor calls this yet — GL-32 owns the privacy decision about what
    /// an unauthenticated caller may see through it, and adds the surface that calls it.
    /// </summary>
    Task<GiftListProjection?> FindByShareTokenAsync(string shareToken, CancellationToken cancellationToken);

    /// <exception cref="GiftListProjectionApplyExhaustedException">
    /// The compare-and-set retry loop exhausted its attempt cap (GL-76) — a sustained, pathological
    /// burst of concurrent writers to this same list.
    /// </exception>
    Task ApplyListCreatedAsync(RecordGiftListCreatedRequest request, CancellationToken cancellationToken);

    /// <exception cref="GiftListProjectionApplyExhaustedException">See <see cref="ApplyListCreatedAsync"/>.</exception>
    Task ApplyListRenamedAsync(RecordGiftListRenamedRequest request, CancellationToken cancellationToken);

    /// <exception cref="GiftListProjectionApplyExhaustedException">See <see cref="ApplyListCreatedAsync"/>.</exception>
    Task ApplyListDeletedAsync(RecordGiftListDeletedRequest request, CancellationToken cancellationToken);

    /// <exception cref="GiftListProjectionApplyExhaustedException">See <see cref="ApplyListCreatedAsync"/>.</exception>
    Task ApplyItemAddedAsync(RecordGiftItemAddedRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Must never simply be a no-op when the item is not yet present — GL-23's specific,
    /// non-hypothetical hazard is a redelivered/reordered remove arriving <em>before</em> its
    /// matching add (both for the same <c>ItemId</c>). If this were a plain "remove by id if
    /// present", that ordering would leave the item to be added later with nothing to say it was
    /// already removed, and the add would resurrect it. The implementation is expected to leave a
    /// tombstone (a removed marker carrying this event's own timestamp) so a later, but
    /// chronologically <em>older</em>, add is recognised as stale and ignored.
    /// </summary>
    /// <exception cref="GiftListProjectionApplyExhaustedException">See <see cref="ApplyListCreatedAsync"/>.</exception>
    Task ApplyItemRemovedAsync(RecordGiftItemRemovedRequest request, CancellationToken cancellationToken);
}
