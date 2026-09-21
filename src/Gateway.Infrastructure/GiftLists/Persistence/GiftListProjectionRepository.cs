using BuildingBlocks.Persistence;
using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.RecordGiftItemAdded;
using Gateway.Application.GiftLists.RecordGiftItemRemoved;
using Gateway.Application.GiftLists.RecordGiftListCreated;
using Gateway.Application.GiftLists.RecordGiftListDeleted;
using Gateway.Application.GiftLists.RecordGiftListRenamed;
using MongoDB.Driver;

namespace Gateway.Infrastructure.GiftLists.Persistence;

/// <summary>
/// Builds <c>gateway.giftListProjections</c> from GiftLists' integration events (GL-23). Every
/// <c>Apply*</c> method is a read-mutate-versioned-replace retry loop (mirrors
/// <c>GiftLists.Infrastructure.GiftLists.Persistence.GiftListRepository.UpdateAsync</c>'s own
/// version-guard idea, GL-64, but retries instead of throwing on an ordinary lost race — see
/// <see cref="GiftListProjectionDocument.Version"/>'s own doc comment for why): read the current
/// document (or none), compute what should change, and only write if something actually would.
/// "Nothing would change" covers both plain redelivery of the same event and a genuinely stale,
/// reordered event (an add older than an already-applied remove for the same item, or a rename
/// older than the name already stored) — <see cref="IGiftListProjectionRepository"/>'s own doc
/// comment has the full reasoning.
///
/// The retry loop is bounded (<see cref="BoundedCasRetryPolicy.MaxAttempts"/>, GL-76) rather
/// than the unbounded <c>while (true)</c> it used to be — a sustained, pathological burst of
/// concurrent writers to one list now fails loudly with
/// <see cref="GiftListProjectionApplyExhaustedException"/> instead of spinning forever. Ordinary
/// contention, bounded by Rebus's own <c>MaxParallelism</c>, is expected to clear well inside the
/// cap — see <see cref="BoundedCasRetryPolicy"/>'s own doc comment for the arithmetic
/// (including this projection's own MaxAttempts derivation — GL-79 moved both this and
/// <see cref="BoundedCasRetry"/> into <c>BuildingBlocks.Persistence</c> so Reservations' own
/// projection (GL-35/36/37) can reuse the mechanism, but that type's own remarks are explicit
/// that the derivation is this projection's, not a value to inherit unreflectively), and
/// <see cref="BoundedCasRetry"/> for the loop shape that consults it.
/// </summary>
internal sealed class GiftListProjectionRepository : IGiftListProjectionRepository
{
    public const string CollectionName = "giftListProjections";

    // GL-79 review (S1): relies on BoundedCasRetryPolicy's default maxAttempts deliberately, not
    // implicitly — the default's own value (8) IS this projection's own derivation (see that
    // type's own remarks), so this is the one caller for which depending on the default is
    // correct rather than an oversight. Any other consumer must pass its own maxAttempts.
    private readonly BoundedCasRetryPolicy _retryPolicy = new();

    private readonly IMongoCollection<GiftListProjectionDocument> _giftListProjections;

    public GiftListProjectionRepository(IMongoDatabase database)
    {
        _giftListProjections = database.GetCollection<GiftListProjectionDocument>(CollectionName);
    }

    public async Task<GiftListProjection?> FindByIdAsync(Guid listId, CancellationToken cancellationToken)
    {
        var document = await _giftListProjections
            .Find(d => d.Id == listId && d.HasCreated && !d.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : GiftListProjectionDocumentMapper.ToProjection(document);
    }

    public async Task<IReadOnlyList<GiftListProjection>> FindByOwnerAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var documents = await _giftListProjections
            .Find(d => d.OwnerId == ownerId && d.HasCreated && !d.IsDeleted)
            .ToListAsync(cancellationToken);

        return documents.Select(GiftListProjectionDocumentMapper.ToProjection).ToList();
    }

    /// <summary>
    /// The cheap belt behind GL-32's real control. <c>ViewGiftListValidator</c> (via <c>ShareTokenFormat</c>) shape-checks
    /// the token at the boundary before the interactor runs, so an empty one cannot arrive through
    /// <c>sharedGiftList(token)</c>; this guard costs one comparison and means any <em>future</em>
    /// caller of this port cannot turn an empty string into a query either. That matters
    /// specifically because a stub row (<c>HasCreated == false</c>, see
    /// <see cref="EnsureIndexesAsync"/>) carries <c>ShareToken == string.Empty</c> — the filter
    /// below already excludes it, and this makes the exclusion not depend on that filter alone.
    /// </summary>
    public async Task<GiftListProjection?> FindByShareTokenAsync(string shareToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shareToken))
        {
            return null;
        }

        var document = await _giftListProjections
            .Find(d => d.ShareToken == shareToken && d.HasCreated && !d.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : GiftListProjectionDocumentMapper.ToProjection(document);
    }

    public Task ApplyListCreatedAsync(RecordGiftListCreatedRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(request.ListId, existing => MutateOnCreated(existing, request), cancellationToken);

    public Task ApplyListRenamedAsync(RecordGiftListRenamedRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(request.ListId, existing => MutateOnRenamed(existing, request), cancellationToken);

    public Task ApplyListDeletedAsync(RecordGiftListDeletedRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(request.ListId, existing => MutateOnDeleted(existing, request), cancellationToken);

    public Task ApplyItemAddedAsync(RecordGiftItemAddedRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(request.ListId, existing => MutateOnItemAdded(existing, request), cancellationToken);

    public Task ApplyItemRemovedAsync(RecordGiftItemRemovedRequest request, CancellationToken cancellationToken) =>
        ApplyAsync(request.ListId, existing => MutateOnItemRemoved(existing, request), cancellationToken);

    /// <summary>
    /// The <c>ownerId</c> and <c>shareToken</c> indexes <c>myGiftLists</c>/<see cref="FindByShareTokenAsync"/>
    /// rely on. Applied at startup, same as GiftLists' own <c>shareToken</c> index (ARCHITECTURE.md "Data model")
    /// — a correctness/performance requirement, not something left to be inferred from application
    /// code (CONVENTIONS.md "Persistence").
    ///
    /// Both are performance indexes, <em>not unique</em> — deliberately, unlike GiftLists' own
    /// unique <c>shareToken_unique</c>. The reason is ownership, not feasibility: GiftLists is the
    /// system of record for token uniqueness (its own creation path rejects a collision before
    /// ever publishing <c>GiftListCreatedV1</c>), so this projection never needs to enforce it
    /// itself — it only needs to be able to find a document that carries one, and a non-unique
    /// index already does that.
    ///
    /// A <em>plain</em> unique index here would additionally be a trap, which is worth naming so
    /// nobody reaches for one out of habit: CONVENTIONS.md "Messaging"'s at-least-once,
    /// out-of-order delivery means <see cref="Stub"/> rows created for an item/rename event that
    /// outraces its own list's <c>GiftListCreatedV1</c> (GL-64) all carry
    /// <see cref="GiftListProjectionDocument.ShareToken"/> <c>== string.Empty</c> until that
    /// Created event lands and fills the real token in. Two different lists can each have such a
    /// stub in flight at once, so a plain unique index on <c>shareToken</c> would make the second
    /// stub's insert fail with a duplicate-key error that <see cref="TryApplyOnceAsync"/> cannot
    /// tell apart from a lost race on the *same* document — it would retry forever against a real
    /// collision, not a transient one, and eventually throw
    /// <see cref="GiftListProjectionApplyExhaustedException"/> for a perfectly legitimate event.
    /// (A partial unique index scoped to <c>HasCreated == true</c> would dodge that specific trap
    /// — the option was available, not foreclosed — but there is still nothing for it to
    /// *enforce*: GiftLists already guarantees at most one materialised document ever carries a
    /// given real token, so a partial-unique index here would only be re-asserting, at strictly
    /// more risk, a guarantee this service does not own.)
    /// </summary>
    public static Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<GiftListProjectionDocument>(CollectionName);
        var ownerIdIndex = new CreateIndexModel<GiftListProjectionDocument>(
            Builders<GiftListProjectionDocument>.IndexKeys.Ascending(d => d.OwnerId),
            new CreateIndexOptions { Name = "ownerId" });
        var shareTokenIndex = new CreateIndexModel<GiftListProjectionDocument>(
            Builders<GiftListProjectionDocument>.IndexKeys.Ascending(d => d.ShareToken),
            new CreateIndexOptions { Name = "shareToken" });

        return collection.Indexes.CreateManyAsync([ownerIdIndex, shareTokenIndex], cancellationToken);
    }

    /// <exception cref="GiftListProjectionApplyExhaustedException">
    /// <see cref="BoundedCasRetryPolicy.MaxAttempts"/> compare-and-set attempts all lost to a
    /// concurrent writer — this project relies on <see cref="BoundedCasRetryPolicy"/>'s own
    /// default (GL-79 review, S1: an instance parameter now, not a shared constant) rather than
    /// stating its own, since the default's own derivation IS this projection's arithmetic; see
    /// that type's own doc comment for the arithmetic, and for why a different consumer must not
    /// do the same, and
    /// <see cref="GiftListProjectionApplyExhaustedException"/>'s for why this throws rather than
    /// giving up silently. <see cref="BoundedCasRetry"/> owns the attempt-count/backoff/throw
    /// shape — including the throw itself now (review, Batch 16 round 3): this method hands it a
    /// factory, not an action, so there is no way to reach this method's own exhaustion path
    /// without an exception actually being thrown.
    /// </exception>
    private Task ApplyAsync(
        Guid listId,
        Func<GiftListProjectionDocument?, (GiftListProjectionDocument Updated, bool Changed)> mutate,
        CancellationToken cancellationToken) =>
        BoundedCasRetry.RunAsync(
            _retryPolicy,
            _ => TryApplyOnceAsync(listId, mutate, cancellationToken),
            exhaustedException: () => new GiftListProjectionApplyExhaustedException(listId, _retryPolicy.MaxAttempts),
            cancellationToken);

    /// <summary>
    /// One compare-and-set attempt: read the current document (or none), compute what should
    /// change, and only write if something actually would. Returns <see langword="true"/> once
    /// there is nothing further to do — a successful write, or "nothing would change" (redelivery
    /// of the same event, or a genuinely stale, reordered one) — and <see langword="false"/> to
    /// mean a concurrent writer's own write landed first, so <see cref="BoundedCasRetry"/> should
    /// retry. Nobody is waiting synchronously on a projection write the way a command caller might
    /// be on an aggregate write (contrast <c>GiftListRepository.UpdateAsync</c>, which throws for
    /// its caller to let Rebus redeliver) — UNLESS every attempt the cap allows is exhausted, which
    /// is <see cref="ApplyAsync"/>'s own concern, not this method's.
    /// </summary>
    private async Task<bool> TryApplyOnceAsync(
        Guid listId,
        Func<GiftListProjectionDocument?, (GiftListProjectionDocument Updated, bool Changed)> mutate,
        CancellationToken cancellationToken)
    {
        var existing = await _giftListProjections
            .Find(d => d.Id == listId)
            .FirstOrDefaultAsync(cancellationToken);

        var (updated, changed) = mutate(existing);
        if (!changed)
        {
            return true;
        }

        if (existing is null)
        {
            try
            {
                await _giftListProjections.InsertOneAsync(WithVersion(updated, 0), cancellationToken: cancellationToken);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another handler instance inserted the stub/row first between our read and our
                // write — retry as a versioned replace against what is there now, same as a lost
                // version-filtered replace below.
                return false;
            }
        }

        var expectedVersion = existing.Version;
        var filter = Builders<GiftListProjectionDocument>.Filter.Where(
            d => d.Id == listId && d.Version == expectedVersion);
        var result = await _giftListProjections.ReplaceOneAsync(
            filter, WithVersion(updated, expectedVersion + 1), cancellationToken: cancellationToken);

        return result.MatchedCount != 0;
    }

    private static GiftListProjectionDocument WithVersion(GiftListProjectionDocument document, long version) => new()
    {
        Id = document.Id,
        OwnerId = document.OwnerId,
        Name = document.Name,
        NameUpdatedAt = document.NameUpdatedAt,
        ExpiresAt = document.ExpiresAt,
        ShareToken = document.ShareToken,
        CreatedAt = document.CreatedAt,
        HasCreated = document.HasCreated,
        IsDeleted = document.IsDeleted,
        DeletedAt = document.DeletedAt,
        Version = version,
        Items = document.Items,
    };

    /// <summary>
    /// A not-yet-fully-created row for an event whose list this projection has not seen a
    /// <c>GiftListCreatedV1</c> for yet (GL-64 can delay/reorder that message like any other).
    /// <see cref="GiftListProjectionDocument.HasCreated"/> stays <see langword="false"/> until
    /// <see cref="MutateOnCreated"/> itself runs, which is what keeps a stub invisible to
    /// <see cref="FindByIdAsync"/>/<see cref="FindByOwnerAsync"/> in the meantime.
    /// </summary>
    private static GiftListProjectionDocument Stub(Guid listId) => new()
    {
        Id = listId,
        OwnerId = Guid.Empty,
        Name = string.Empty,
        NameUpdatedAt = DateTime.MinValue,
        ExpiresAt = DateTime.MinValue,
        ShareToken = string.Empty,
        CreatedAt = DateTime.MinValue,
        HasCreated = false,
        IsDeleted = false,
        DeletedAt = null,
        Version = 0,
        Items = [],
    };

    private static (GiftListProjectionDocument, bool) MutateOnCreated(
        GiftListProjectionDocument? existing, RecordGiftListCreatedRequest request)
    {
        var createdAt = ProjectionInstants.ToStoredPrecision(request.CreatedAt);
        var baseline = existing ?? Stub(request.ListId);

        // Guard Name specifically: a stub created by an out-of-order Renamed/Item event already
        // stamped a real name, or (on redelivery) a later Renamed already advanced past this
        // Created's own original name. Every other field below has no other writer, ever, so
        // Created always applies them unconditionally.
        // `>`, not `>=`: MutateOnRenamed treats a tie as "the rename applies", so Created had to
        // agree or the projection's settled state depended on delivery order — rename at T then
        // Created at T reverted the name (Batch 13 review). A fresh Stub's NameUpdatedAt is
        // DateTime.MinValue, which every real timestamp still exceeds, so the out-of-order path is
        // unaffected; and a Created redelivery now computes no change rather than reapplying.
        var applyName = createdAt > baseline.NameUpdatedAt;

        var updated = new GiftListProjectionDocument
        {
            Id = request.ListId,
            OwnerId = request.OwnerId,
            Name = applyName ? request.Name : baseline.Name,
            NameUpdatedAt = applyName ? createdAt : baseline.NameUpdatedAt,
            ExpiresAt = ProjectionInstants.ToStoredPrecision(request.ExpiresAt),
            ShareToken = request.ShareToken,
            CreatedAt = createdAt,
            HasCreated = true,
            IsDeleted = baseline.IsDeleted,
            DeletedAt = baseline.DeletedAt,
            Version = baseline.Version,
            Items = baseline.Items,
        };

        var changed = !baseline.HasCreated ||
            baseline.OwnerId != updated.OwnerId ||
            baseline.Name != updated.Name ||
            baseline.NameUpdatedAt != updated.NameUpdatedAt ||
            baseline.ExpiresAt != updated.ExpiresAt ||
            baseline.ShareToken != updated.ShareToken ||
            baseline.CreatedAt != updated.CreatedAt;

        return (updated, changed);
    }

    private static (GiftListProjectionDocument, bool) MutateOnRenamed(
        GiftListProjectionDocument? existing, RecordGiftListRenamedRequest request)
    {
        var renamedAt = ProjectionInstants.ToStoredPrecision(request.RenamedAt);
        var baseline = existing ?? Stub(request.ListId);

        // Stale: a later rename (or the original creation name, if this event is somehow older
        // than the list's own creation timestamp) already won. existing is guaranteed non-null
        // here — a fresh Stub's NameUpdatedAt is DateTime.MinValue, which no real timestamp is
        // ever less than.
        if (renamedAt < baseline.NameUpdatedAt)
        {
            return (baseline, false);
        }

        if (renamedAt == baseline.NameUpdatedAt && baseline.Name == request.Name)
        {
            return (baseline, false); // exact redelivery
        }

        var updated = new GiftListProjectionDocument
        {
            Id = baseline.Id,
            OwnerId = baseline.OwnerId,
            Name = request.Name,
            NameUpdatedAt = renamedAt,
            ExpiresAt = baseline.ExpiresAt,
            ShareToken = baseline.ShareToken,
            CreatedAt = baseline.CreatedAt,
            HasCreated = baseline.HasCreated,
            IsDeleted = baseline.IsDeleted,
            DeletedAt = baseline.DeletedAt,
            Version = baseline.Version,
            Items = baseline.Items,
        };
        return (updated, true);
    }

    private static (GiftListProjectionDocument, bool) MutateOnDeleted(
        GiftListProjectionDocument? existing, RecordGiftListDeletedRequest request)
    {
        var deletedAt = ProjectionInstants.ToStoredPrecision(request.DeletedAt);
        var baseline = existing ?? Stub(request.ListId);

        if (baseline.DeletedAt is { } existingDeletedAt && existingDeletedAt >= deletedAt)
        {
            return (baseline, false); // already deleted at this time or later
        }

        var updated = new GiftListProjectionDocument
        {
            Id = baseline.Id,
            OwnerId = baseline.OwnerId,
            Name = baseline.Name,
            NameUpdatedAt = baseline.NameUpdatedAt,
            ExpiresAt = baseline.ExpiresAt,
            ShareToken = baseline.ShareToken,
            CreatedAt = baseline.CreatedAt,
            HasCreated = baseline.HasCreated,
            IsDeleted = true,
            DeletedAt = deletedAt,
            Version = baseline.Version,
            Items = baseline.Items,
        };
        return (updated, true);
    }

    private static (GiftListProjectionDocument, bool) MutateOnItemAdded(
        GiftListProjectionDocument? existing, RecordGiftItemAddedRequest request)
    {
        var baseline = existing ?? Stub(request.ListId);
        var (items, changed) = UpsertOnAdded(
            baseline.Items, request.ItemId, ProjectionInstants.ToStoredPrecision(request.AddedAt), request.Name, request.Description, request.Url);

        return changed ? (WithItems(baseline, items), true) : (baseline, false);
    }

    private static (GiftListProjectionDocument, bool) MutateOnItemRemoved(
        GiftListProjectionDocument? existing, RecordGiftItemRemovedRequest request)
    {
        var baseline = existing ?? Stub(request.ListId);
        var (items, changed) = UpsertOnRemoved(baseline.Items, request.ItemId, ProjectionInstants.ToStoredPrecision(request.RemovedAt));

        return changed ? (WithItems(baseline, items), true) : (baseline, false);
    }

    private static GiftListProjectionDocument WithItems(
        GiftListProjectionDocument baseline, List<GiftItemProjectionDocument> items) => new()
    {
        Id = baseline.Id,
        OwnerId = baseline.OwnerId,
        Name = baseline.Name,
        NameUpdatedAt = baseline.NameUpdatedAt,
        ExpiresAt = baseline.ExpiresAt,
        ShareToken = baseline.ShareToken,
        CreatedAt = baseline.CreatedAt,
        HasCreated = baseline.HasCreated,
        IsDeleted = baseline.IsDeleted,
        DeletedAt = baseline.DeletedAt,
        Version = baseline.Version,
        Items = items,
    };

    /// <summary>
    /// Item-keyed upsert, never an append (CONVENTIONS.md "Messaging" review, GL-23) — an append would
    /// duplicate the item on redelivery. Last-write-wins on <see cref="GiftItemProjectionDocument.UpdatedAt"/>:
    /// an existing entry strictly newer than <paramref name="addedAt"/> already reflects a later
    /// fact (most sharply, an already-applied remove for the same item — see this type's own
    /// "the real hazard" doc comment on <see cref="IGiftListProjectionRepository"/>) and must not
    /// be overwritten by an older add arriving late.
    /// </summary>
    private static (List<GiftItemProjectionDocument> Items, bool Changed) UpsertOnAdded(
        List<GiftItemProjectionDocument> items, Guid itemId, DateTime addedAt, string name, string? description, string? url)
    {
        var index = items.FindIndex(i => i.ItemId == itemId);
        var candidate = new GiftItemProjectionDocument
        {
            ItemId = itemId,
            Name = name,
            Description = description,
            Url = url,
            IsRemoved = false,
            UpdatedAt = addedAt,
        };

        if (index < 0)
        {
            return (new List<GiftItemProjectionDocument>(items) { candidate }, true);
        }

        var current = items[index];

        // A TIE MUST GO TO THE TOMBSTONE, not just a strictly-later event. `>` alone let an add
        // sharing a remove's exact timestamp fall through and overwrite the tombstone, resurrecting
        // a deleted gift — demonstrated in the Batch 13 review. GL-66 is what made that reachable:
        // at 100ns tick resolution an exact tie was vanishingly unlikely, and at the millisecond
        // resolution GL-66 normalised these timestamps to, it is an ordinary collision. Two agents
        // in one batch, neither compiling against the other, meeting exactly here.
        //
        // UpsertOnRemoved already resolves the mirror case in favour of the remove, so deciding
        // ties the same way makes the pair CONVERGENT: add-then-remove and remove-then-add settle
        // on the same state regardless of arrival order, which is the property the read model
        // actually needs — being merely "idempotent" never gave it.
        if (current.UpdatedAt > addedAt || (current.UpdatedAt == addedAt && current.IsRemoved))
        {
            return (items, false); // a chronologically later event (most sharply, a remove) already applied
        }

        if (current.UpdatedAt == addedAt && !current.IsRemoved &&
            current.Name == name && current.Description == description && current.Url == url)
        {
            return (items, false); // exact redelivery
        }

        var replaced = new List<GiftItemProjectionDocument>(items);
        replaced[index] = candidate;
        return (replaced, true);
    }

    /// <summary>
    /// Never deletes the array entry — see <see cref="GiftItemProjectionDocument.IsRemoved"/>'s
    /// own doc comment for why a tombstone must be left behind even (especially) when this item
    /// has never been seen before: that is exactly the case where the matching
    /// <c>GiftItemAddedV1</c> is still in flight (GL-64) and must lose when it eventually arrives.
    /// </summary>
    private static (List<GiftItemProjectionDocument> Items, bool Changed) UpsertOnRemoved(
        List<GiftItemProjectionDocument> items, Guid itemId, DateTime removedAt)
    {
        var index = items.FindIndex(i => i.ItemId == itemId);
        if (index < 0)
        {
            var tombstone = new GiftItemProjectionDocument
            {
                ItemId = itemId,
                Name = string.Empty,
                Description = null,
                Url = null,
                IsRemoved = true,
                UpdatedAt = removedAt,
            };
            return (new List<GiftItemProjectionDocument>(items) { tombstone }, true);
        }

        var current = items[index];
        if (current.UpdatedAt > removedAt)
        {
            return (items, false);
        }

        if (current.UpdatedAt == removedAt && current.IsRemoved)
        {
            return (items, false); // exact redelivery
        }

        var updated = new GiftItemProjectionDocument
        {
            ItemId = itemId,
            Name = current.Name,
            Description = current.Description,
            Url = current.Url,
            IsRemoved = true,
            UpdatedAt = removedAt,
        };
        var replaced = new List<GiftItemProjectionDocument>(items);
        replaced[index] = updated;
        return (replaced, true);
    }
}
