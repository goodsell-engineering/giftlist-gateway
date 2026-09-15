namespace Gateway.Application.GiftLists;

/// <summary>
/// Thrown when <c>GiftListProjectionRepository.ApplyAsync</c>'s compare-and-set retry loop
/// (<see cref="IGiftListProjectionRepository"/>'s five <c>Apply*</c> methods, GL-23) exhausts its
/// attempt cap (GL-76) — a sustained, pathological burst of concurrent writers to ONE list that
/// keeps beating this attempt's read every single time.
/// </summary>
/// <remarks>
/// <para>
/// Same reasoning as GiftLists' own <c>GiftListConcurrencyException</c> (contention losing a race
/// is not a business outcome an end user did anything wrong to trigger, so it is not a
/// <c>Result</c>/error code) but a different shape: that type is raised after exactly one lost
/// attempt and relies entirely on Rebus's own redelivery to retry. This loop already retries
/// itself — the cap exists only to bound a retry loop that, before GL-76, was an unbounded
/// <c>while (true)</c> with no ceiling — so by the time this is thrown, the attempt cap's worth of
/// retries have already happened <em>inside</em> one delivery. Uncaught here (CONVENTIONS.md "Messaging":
/// no business logic, and no reason for a thin Rebus handler to catch this to retry — that is
/// exactly what Rebus's own redelivery already does), it propagates through the interactor and the
/// thin handler into Rebus's error-queue path: a real throw, not a swallowed failure, because
/// returning normally without having written the change would be silent data loss where today
/// there is none (GL-76 report).
/// </para>
/// <para>
/// Each Rebus-level redelivery starts this loop's attempt count fresh, so a burst has to stay
/// pathological across an entire additional delivery attempt (not just across one single CAS
/// retry) to end up here more than once — this is a circuit against a genuinely sustained
/// contention storm on one list, not a hair-trigger.
/// </para>
/// </remarks>
public sealed class GiftListProjectionApplyExhaustedException(Guid listId, int attempts)
    : Exception(
        $"Gift list projection '{listId}' could not be written after {attempts} attempts — " +
        "another writer kept winning the compare-and-set race every time. Failing loudly rather " +
        "than spinning forever; Rebus's own redelivery will retry this event with a fresh attempt " +
        "budget.")
{
    public Guid ListId { get; } = listId;

    public int Attempts { get; } = attempts;
}
