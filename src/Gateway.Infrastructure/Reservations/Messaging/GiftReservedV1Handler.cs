using Gateway.Application.Common;
using Gateway.Application.Reservations.RecordGiftReserved;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Pipeline;
using Reservations.Contracts.Reservations.Events;

namespace Gateway.Infrastructure.Reservations.Messaging;

/// <summary>
/// Thin by design — see <c>GiftLists.Messaging.GiftListCreatedV1Handler</c> for the rationale.
/// The anti-corruption layer for Reservations' one event (ARCHITECTURE.md "Consuming other
/// services' events: anti-corruption layer"): <see cref="GiftReservedV1"/> has three fields and
/// the request it becomes has two. <c>ReservedAt</c> is dropped here, at the boundary, and never
/// reaches Application or Mongo — <c>ReservationProjection</c>'s own doc comment has the reason.
/// </summary>
internal sealed class GiftReservedV1Handler(
    IInteractor<RecordGiftReservedRequest, RecordGiftReservedResponse> recordGiftReserved)
    : IHandleMessages<GiftReservedV1>
{
    public async Task Handle(GiftReservedV1 message)
    {
        var cancellationToken = MessageContext.Current.GetCancellationToken();
        var request = new RecordGiftReservedRequest(message.ListId, message.ItemId);
        await recordGiftReserved.Handle(request, cancellationToken);
    }
}
