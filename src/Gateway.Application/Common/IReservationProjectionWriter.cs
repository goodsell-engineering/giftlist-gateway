namespace Gateway.Application.Common;

/// <summary>
/// The write side of the reservation projection: records that one item on one list is reserved.
/// Kept apart from the read port (<c>Gateway.Application.Reservations.IReservationProjectionRepository</c>)
/// so that the interactor behind <c>GiftReservedV1</c> holds nothing that could read reservation
/// state back out — see that port's own doc comment for why the read side is one type's
/// dependency and no other's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this sits in <c>Common/</c> rather than beside <c>Reservations/</c>.</b> Not because
/// it is domain-agnostic — it plainly is not. Every <c>I*</c> interface in the Application ring
/// outside <c>Common/</c> whose name does not end in <c>Repository</c> is treated by the synced
/// architecture suite as a <em>named input port</em>: it must then have a matching
/// <c>*Interactor</c>/<c>*Request</c> pair and may never be injected anywhere
/// (<c>NamingConventionTests.InputPorts_ShouldHaveAMatchingInteractorAndRequest</c>,
/// <c>InputPortInjectionRuleTests</c>). A collaborator port that is injected therefore has two
/// homes: a <c>Repository</c> name, or <c>Common/</c>. This is a writer, not a repository — it
/// takes and returns no aggregate, and calling it one would make
/// <c>RepositoryPorts_ShouldReturnAggregatesNeverDocuments</c>'s premise false — so it lives
/// here, the same way <c>GiftLists.Application.Common.IShareTokenGenerator</c>,
/// <c>Identity.Application.Common.IPasswordHasher</c> and
/// <c>Reservations.Application.Common.IReleaseSecretGenerator</c> do in their services.
/// </para>
/// <para>
/// Must be safe to run more than once, by construction (CONVENTIONS.md "Messaging": delivery is
/// at-least-once; projections are upserts, never blind inserts). The implementation is expected
/// to upsert keyed on the (list, item) pair the event is about.
/// </para>
/// </remarks>
public interface IReservationProjectionWriter
{
    Task ApplyGiftReservedAsync(Guid listId, Guid itemId, CancellationToken cancellationToken);
}
