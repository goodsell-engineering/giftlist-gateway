using Gateway.Application.Common;
using Gateway.Application.GiftLists.RecordGiftListDeleted;
using GiftLists.Contracts.GiftLists.Events;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Gateway.Infrastructure.GiftLists.Messaging;

/// <summary>Thin by design — see <see cref="GiftListCreatedV1Handler"/> for the rationale.</summary>
internal sealed class GiftListDeletedV1Handler(
    IInteractor<RecordGiftListDeletedRequest, RecordGiftListDeletedResponse> recordGiftListDeleted)
    : IHandleMessages<GiftListDeletedV1>
{
    public async Task Handle(GiftListDeletedV1 message)
    {
        var cancellationToken = MessageContext.Current.GetCancellationToken();
        var request = new RecordGiftListDeletedRequest(message.ListId, message.DeletedAt);
        await recordGiftListDeleted.Handle(request, cancellationToken);
    }
}
