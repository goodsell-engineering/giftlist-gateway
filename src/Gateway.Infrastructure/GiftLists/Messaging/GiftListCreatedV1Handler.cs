using Gateway.Application.Common;
using Gateway.Application.GiftLists.RecordGiftListCreated;
using GiftLists.Contracts.GiftLists.Events;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Gateway.Infrastructure.GiftLists.Messaging;

/// <summary>
/// Thin by design (CONVENTIONS.md "Messaging" — no business logic in a handler): translate the wire event
/// into <see cref="RecordGiftListCreatedRequest"/> and call the one input port. This is also the
/// ACL boundary (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer") — <see cref="GiftListCreatedV1"/> is GiftLists' Contracts
/// type and stops here; nothing past this line ever sees it. Depends on the generic
/// <see cref="IInteractor{TRequest,TResponse}"/>, not <see cref="IRecordGiftListCreated"/> itself
/// — see that port's own doc comment for why nothing resolves it from the container.
/// </summary>
internal sealed class GiftListCreatedV1Handler(
    IInteractor<RecordGiftListCreatedRequest, RecordGiftListCreatedResponse> recordGiftListCreated)
    : IHandleMessages<GiftListCreatedV1>
{
    public async Task Handle(GiftListCreatedV1 message)
    {
        var cancellationToken = MessageContext.Current.GetCancellationToken();
        var request = new RecordGiftListCreatedRequest(
            message.ListId, message.OwnerId, message.Name, message.ExpiresAt, message.ShareToken, message.CreatedAt);
        await recordGiftListCreated.Handle(request, cancellationToken);
    }
}
