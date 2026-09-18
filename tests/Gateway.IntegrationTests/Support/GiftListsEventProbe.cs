using System.Collections.Concurrent;
using Rebus.Handlers;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// GL-73: counts how many times each upstream integration event type (GiftLists', and since
/// GL-38 Reservations' <c>GiftReservedV1</c>) has actually been handled by the real Gateway consumer — registered as an <em>additional</em> Rebus handler alongside
/// the production ones via <see cref="GiftListsEventProbeHandler{TEvent}"/> (Rebus supports and
/// runs every <see cref="IHandleMessages{TMessage}"/> registered for a message type, and only
/// acks once all of them have completed), so a count here can only have advanced as part of the
/// very same at-least-once delivery unit as the production handler's own processing.
///
/// This exists because <c>GiftListProjectionRepository</c>'s redelivery handling is correctly
/// idempotent (CONVENTIONS.md "Messaging"): a content-identical redelivery causes no Mongo write at
/// all, so the projection document — including its own <c>Version</c> — is unchanged whether the
/// message was processed or never sent in the first place. Polling that document can never tell
/// the two apart; this counter, being orthogonal to what the handler's business logic decides to
/// do, can.
/// </summary>
public sealed class GiftListsEventProbe
{
    private readonly ConcurrentDictionary<Type, int> _counts = new();

    public void Record<TEvent>() => _counts.AddOrUpdate(typeof(TEvent), 1, static (_, count) => count + 1);

    public int CountFor<TEvent>() => _counts.TryGetValue(typeof(TEvent), out var count) ? count : 0;
}

/// <summary>See <see cref="GiftListsEventProbe"/>.</summary>
internal sealed class GiftListsEventProbeHandler<TEvent>(GiftListsEventProbe probe) : IHandleMessages<TEvent>
{
    public Task Handle(TEvent message)
    {
        probe.Record<TEvent>();
        return Task.CompletedTask;
    }
}
