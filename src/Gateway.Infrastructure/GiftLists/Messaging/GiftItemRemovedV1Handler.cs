using Gateway.Application.Common;
using Gateway.Application.GiftLists.RecordGiftItemRemoved;
using GiftLists.Contracts.GiftLists.Events;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Gateway.Infrastructure.GiftLists.Messaging;

/// <summary>Thin by design — see <see cref="GiftListCreatedV1Handler"/> for the rationale.</summary>
internal sealed class GiftItemRemovedV1Handler(
    IInteractor<RecordGiftItemRemovedRequest, RecordGiftItemRemovedResponse> recordGiftItemRemoved)
    : IHandleMessages<GiftItemRemovedV1>
{
    public async Task Handle(GiftItemRemovedV1 message)
    {
        var cancellationToken = MessageContext.Current.GetCancellationToken();
        var request = new RecordGiftItemRemovedRequest(message.ListId, message.ItemId, message.RemovedAt);
        await recordGiftItemRemoved.Handle(request, cancellationToken);
    }
}
