using Gateway.Application.Common;
using Gateway.Application.GiftLists.RecordGiftListRenamed;
using GiftLists.Contracts.GiftLists.Events;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Gateway.Infrastructure.GiftLists.Messaging;

/// <summary>Thin by design — see <see cref="GiftListCreatedV1Handler"/> for the rationale.</summary>
internal sealed class GiftListRenamedV1Handler(
    IInteractor<RecordGiftListRenamedRequest, RecordGiftListRenamedResponse> recordGiftListRenamed)
    : IHandleMessages<GiftListRenamedV1>
{
    public async Task Handle(GiftListRenamedV1 message)
    {
        var cancellationToken = MessageContext.Current.GetCancellationToken();
        var request = new RecordGiftListRenamedRequest(message.ListId, message.Name, message.RenamedAt);
        await recordGiftListRenamed.Handle(request, cancellationToken);
    }
}
