using Gateway.Application.GiftLists;

namespace Gateway.Infrastructure.GiftLists.Persistence;

/// <summary>Source + "To" + target (CONVENTIONS.md "Naming").</summary>
internal static class GiftListProjectionDocumentMapper
{
    /// <summary>
    /// Removed items (tombstones — see <see cref="GiftItemProjectionDocument.IsRemoved"/>'s own
    /// doc comment) are filtered out here, never on the way in: this is the one place that
    /// decides what a query response looks like, so it is the one place a future second query
    /// cannot forget the filter.
    /// </summary>
    public static GiftListProjection ToProjection(GiftListProjectionDocument document) => new(
        document.Id,
        document.OwnerId,
        document.Name,
        new DateTimeOffset(document.ExpiresAt, TimeSpan.Zero),
        document.ShareToken,
        new DateTimeOffset(document.CreatedAt, TimeSpan.Zero),
        document.Items
            .Where(item => !item.IsRemoved)
            .Select(item => new GiftItemProjection(item.ItemId, item.Name, item.Description, item.Url))
            .ToList());
}
