namespace Gateway.Infrastructure.GiftLists.Persistence;

/// <summary>
/// The Mongo-facing shape of the Gateway's own read model (<c>gateway.giftListProjections</c>),
/// kept separate from <c>Gateway.Application.GiftLists.GiftListProjection</c> (ARCHITECTURE.md "Data that crosses boundaries"
/// — no <c>[Bson*]</c> attributes in Application; none needed here either — <see cref="Id"/>
/// auto-maps to <c>_id</c> and every field name camelCases via the shared
/// <c>BuildingBlocks.Persistence.MongoConventions</c> pack).
///
/// This document can exist in a partially-filled state a
/// <c>GiftLists.Domain.GiftLists.GiftList</c> aggregate never can: GL-64's redelivery hazard means
/// a <c>GiftListRenamedV1</c>/<c>GiftItemAddedV1</c>/<c>GiftItemRemovedV1</c> can be processed for
/// a <see cref="Id"/> the Gateway has not yet seen a <c>GiftListCreatedV1</c> for (that event's own
/// message can just as easily be the one stuck being retried). Rather than drop those events on
/// the floor with nothing to replay them from, <see cref="GiftListProjectionRepository"/> creates
/// a stub row for them to land on — <see cref="OwnerId"/>/<see cref="ExpiresAt"/>/
/// <see cref="ShareToken"/>/<see cref="CreatedAt"/> read as their type's default until
/// <c>GiftListCreatedV1</c> itself arrives and fills them in unconditionally (nothing else ever
/// writes them, so there is no ordering hazard to guard there). A stub is invisible to
/// <c>myGiftLists</c>/<c>giftList(id)</c> regardless (<see cref="OwnerId"/> reads
/// <see cref="Guid.Empty"/>, which cannot equal any real caller's id, and
/// <see cref="GiftListProjectionRepository.FindByIdAsync"/> only ever matches on the real
/// <see cref="Id"/> anyway — <c>giftList(id)</c> would still fail ownership rather than leak it).
/// </summary>
public sealed class GiftListProjectionDocument
{
    public required Guid Id { get; init; }

    public required Guid OwnerId { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Last-write-wins guard for <see cref="Name"/> — set from <c>GiftListCreatedV1.CreatedAt</c>
    /// on creation, then only advanced by a <c>GiftListRenamedV1</c> whose own
    /// <c>RenamedAt</c> is at least this recent. Two renames delivered out of order (GL-64) is
    /// exactly what this guards against — see
    /// <see cref="GiftListProjectionRepository"/>'s own doc comment.
    /// </summary>
    public required DateTime NameUpdatedAt { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public required string ShareToken { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// <see langword="false"/> for a stub row created by an out-of-order Renamed/Item event that
    /// arrived before this list's own <c>GiftListCreatedV1</c> — see this type's own doc comment.
    /// <see cref="GiftListProjectionRepository.FindByIdAsync"/>/<c>FindByOwnerAsync</c> both
    /// require this to be <see langword="true"/>, which is what keeps a stub invisible to
    /// <c>myGiftLists</c>/<c>giftList(id)</c> until the real creation event lands.
    /// </summary>
    public required bool HasCreated { get; init; }

    public required bool IsDeleted { get; init; }

    /// <summary>Last-write-wins guard for <see cref="IsDeleted"/>, mirroring <see cref="NameUpdatedAt"/>. Null until a <c>GiftListDeletedV1</c> has been applied.</summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>
    /// Optimistic-concurrency counter, same idea as <c>GiftListDocument.Version</c> (GL-64) but
    /// for this projection: <see cref="GiftListProjectionRepository"/> reads, mutates and
    /// version-filters its replace, retrying on a lost race rather than throwing — nobody is
    /// waiting synchronously on a projection write the way a command caller might be on an
    /// aggregate write, so a retry loop is the simpler choice here than propagating a concurrency
    /// exception for Rebus to redeliver.
    /// </summary>
    public required long Version { get; init; }

    public required List<GiftItemProjectionDocument> Items { get; init; }
}
