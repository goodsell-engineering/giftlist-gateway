using Gateway.Application.Common;
using Gateway.Application.Reservations;
using MongoDB.Driver;

namespace Gateway.Infrastructure.Reservations.Persistence;

/// <summary>
/// Builds and reads <c>gateway.reservationProjections</c> (GL-38) — the second projection in this
/// service, alongside <c>GiftLists.Persistence.GiftListProjectionRepository</c>, and a far
/// simpler one: a document here is immutable once written, so the write is a plain upsert keyed
/// on the (list, item) pair rather than that repository's read-mutate-versioned-replace loop.
/// There is nothing to compare-and-set because there is nothing to mutate.
/// </summary>
/// <remarks>
/// One class behind two ports, on purpose. <see cref="IReservationProjectionRepository"/> (read)
/// is injected into <c>ViewGiftListInteractor</c> and nothing else;
/// <see cref="IReservationProjectionWriter"/> (write) is injected into the pass-through behind
/// <c>GiftReservedV1Handler</c>. Splitting the ports is what lets
/// <c>ReservationProjectionPortIsolationTests</c> name exactly one consumer of reservation data
/// (ARCHITECTURE.md "Defence in depth on the owner-facing path"); splitting the class as well
/// would only duplicate the collection handle.
/// </remarks>
internal sealed class ReservationProjectionRepository : IReservationProjectionRepository, IReservationProjectionWriter
{
    public const string CollectionName = "reservationProjections";

    private readonly IMongoCollection<ReservationProjectionDocument> _reservationProjections;

    public ReservationProjectionRepository(IMongoDatabase database)
    {
        _reservationProjections = database.GetCollection<ReservationProjectionDocument>(CollectionName);
    }

    public async Task<IReadOnlyList<ReservationProjection>> FindByListAsync(Guid listId, CancellationToken cancellationToken)
    {
        var documents = await _reservationProjections
            .Find(d => d.ListId == listId)
            .ToListAsync(cancellationToken);

        return documents.Select(d => new ReservationProjection(d.ListId, d.ItemId)).ToList();
    }

    /// <summary>
    /// An upsert on the pair's own <c>_id</c> (CONVENTIONS.md "Messaging": upserts keyed on the
    /// entity the event is about, never a blind insert). A redelivery replaces the document with
    /// an identical one — matched, not modified — and two concurrent deliveries racing on the
    /// same key can surface as a duplicate-key error from the loser's upsert, which is the
    /// winner's own write and so is treated as done rather than retried or rethrown.
    /// </summary>
    public async Task ApplyGiftReservedAsync(Guid listId, Guid itemId, CancellationToken cancellationToken)
    {
        var document = new ReservationProjectionDocument
        {
            Id = KeyFor(listId, itemId),
            ListId = listId,
            ItemId = itemId,
        };

        try
        {
            await _reservationProjections.ReplaceOneAsync(
                d => d.Id == document.Id,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another delivery of the same event inserted this exact document first.
        }
    }

    /// <summary>
    /// The <c>listId</c> index <see cref="FindByListAsync"/> relies on — applied at startup and
    /// declared beside its repository (CONVENTIONS.md "Persistence"). A performance index, not a
    /// unique one: uniqueness of the pair is already the document's <c>_id</c>.
    /// </summary>
    public static Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<ReservationProjectionDocument>(CollectionName);
        var listIdIndex = new CreateIndexModel<ReservationProjectionDocument>(
            Builders<ReservationProjectionDocument>.IndexKeys.Ascending(d => d.ListId),
            new CreateIndexOptions { Name = "listId" });

        return collection.Indexes.CreateOneAsync(listIdIndex, cancellationToken: cancellationToken);
    }

    private static string KeyFor(Guid listId, Guid itemId) => $"{listId:N}:{itemId:N}";
}
