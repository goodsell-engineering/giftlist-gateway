namespace Gateway.Infrastructure.GiftLists.Persistence;

/// <summary>
/// The embedded, per-item shape inside <see cref="GiftListProjectionDocument.Items"/>. Kept
/// separate from <c>Gateway.Application.GiftLists.GiftItemProjection</c> (ARCHITECTURE.md "Data that crosses boundaries" — no
/// <c>[Bson*]</c> attributes reach Application; none are needed here either, same reasoning as
/// <c>GiftLists.Infrastructure.GiftLists.Persistence.GiftItemDocument</c>).
///
/// A removed item is never deleted from this array — <see cref="IsRemoved"/> plus
/// <see cref="UpdatedAt"/> is a tombstone (GL-23 review, Batch 12): the specific hazard this
/// exists for is a <c>GiftItemRemovedV1</c> being processed before its matching
/// <c>GiftItemAddedV1</c> for the same <see cref="ItemId"/> (GL-64 redelivery can reorder them).
/// If removal deleted the array entry outright, that add would later arrive to an empty slot and
/// re-add an item that was already (chronologically) removed. Keeping the tombstone means the
/// add's own last-write-wins guard — <see cref="UpdatedAt"/> already newer than the add's own
/// timestamp — rejects it instead. <see cref="GiftListProjectionDocumentMapper.ToProjection"/>
/// filters every <see cref="IsRemoved"/> item out before this ever reaches a query response, so a
/// removed item is invisible to <c>myGiftLists</c>/<c>giftList(id)</c> either way.
/// </summary>
public sealed class GiftItemProjectionDocument
{
    public required Guid ItemId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? Url { get; init; }

    public required bool IsRemoved { get; init; }

    /// <summary>
    /// The timestamp of whichever of <c>GiftItemAddedV1.AddedAt</c> /
    /// <c>GiftItemRemovedV1.RemovedAt</c> last legitimately applied to this item — the
    /// last-write-wins guard <see cref="GiftListProjectionRepository"/> compares an incoming
    /// event's own timestamp against before applying it. <see cref="DateTime"/>, not the wire
    /// contract's <see cref="DateTimeOffset"/> — see <c>GiftListDocument.ExpiresAt</c>'s own doc
    /// comment (both services share the same reasoning: always UTC already, so nothing is lost).
    /// </summary>
    public required DateTime UpdatedAt { get; init; }
}
