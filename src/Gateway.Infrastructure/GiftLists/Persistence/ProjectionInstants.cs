namespace Gateway.Infrastructure.GiftLists.Persistence;

/// <summary>
/// Normalises an instant arriving on the wire to the precision this projection can actually store,
/// so ordering decisions compare like with like.
/// </summary>
/// <remarks>
/// <para>
/// The projection documents store instants as BSON <see cref="DateTime"/>, which is MILLISECOND
/// precision; a <see cref="DateTimeOffset"/> off the wire carries 100ns ticks. Without this, every
/// last-write-wins guard compared a value read back from Mongo (truncated) against an incoming one
/// (not truncated) — two different resolutions.
/// </para>
/// <para>
/// That is not only a tie-breaking nuisance, it inverts real orderings. A remove stored at
/// <c>12:00:00.0007</c> reads back as <c>12:00:00.000</c>; an add at <c>12:00:00.0003</c> — which
/// genuinely happened BEFORE the remove — then compares as later and resurrects the deleted item.
/// Truncating on the way in makes the comparison sound in both directions and makes an exact tie
/// exact, which is what lets the tie rules in this file be stated at all.
/// </para>
/// <para>
/// GiftLists normalises its own event timestamps to the same resolution (GL-66), so in practice
/// these arrive aligned already. This does NOT rely on that: the producer's precision is another
/// service's implementation detail travelling over a versioned contract, and after the repo split
/// it is in a different repository entirely. A consumer that silently depends on it would break
/// quietly and remotely.
/// </para>
/// <para>
/// Mirrors <c>GiftLists.Domain.Common.Timestamps</c> in intent. It is deliberately NOT shared: that
/// one is a statement about the GiftList domain's own resolution, this one is a statement about
/// what this projection's storage can represent, and the two happening to be a millisecond is a
/// coincidence rather than a coupling.
/// </para>
/// </remarks>
internal static class ProjectionInstants
{
    /// <summary>Converts to UTC and drops sub-millisecond ticks. Truncates, never rounds, so a
    /// value can only move toward the past.</summary>
    public static DateTime ToStoredPrecision(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));
    }
}
