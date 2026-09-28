using Gateway.Application.Common;
using Gateway.Application.GiftLists.RecordGiftItemDescriptionChanged;
using GiftLists.Contracts.GiftLists.Events;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Gateway.Infrastructure.GiftLists.Messaging;

/// <summary>Thin by design — see <see cref="GiftListCreatedV1Handler"/> for the rationale.</summary>
internal sealed class GiftItemDescriptionChangedV1Handler(
    IInteractor<RecordGiftItemDescriptionChangedRequest, RecordGiftItemDescriptionChangedResponse> recordGiftItemDescriptionChanged)
    : IHandleMessages<GiftItemDescriptionChangedV1>
{
    public async Task Handle(GiftItemDescriptionChangedV1 message)
    {
        var cancellationToken = MessageContext.Current.GetCancellationToken();
        var request = new RecordGiftItemDescriptionChangedRequest(
            message.ListId, message.ItemId, message.Description, message.ChangedAt);
        await recordGiftItemDescriptionChanged.Handle(request, cancellationToken);
    }
}
