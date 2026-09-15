using Gateway.Application.GiftLists;
using Gateway.Infrastructure.Platform.Security;
using Gateway.Infrastructure.Platform.Transport;
using GiftLists.Contracts.GiftLists;
using Grpc.Core;
using Rebus.Bus;

namespace Gateway.Infrastructure.GiftLists.Grpc;

/// <summary>
/// The browser's grpc-web entry point for the GiftLists command surface (GL-71) — mirrors
/// <c>AuthGrpcService</c>'s own shape exactly: thin translation, no local use case, because the
/// Gateway has no aggregate of its own to mutate here either. Unlike <c>AuthGrpcService</c>,
/// every RPC below is fire-and-forget (<see cref="IBus.Send"/>, never
/// <see cref="BuildingBlocks.Messaging.RequestReply.IRequestReplyBridge"/>) — GiftLists'
/// handlers deliberately never <c>bus.Reply</c> (<c>CreateGiftListHandler</c>'s own remarks,
/// ARCHITECTURE.md "Command → event flow"), so there is no reply to await or translate back; each method returns
/// as soon as the command is accepted onto the bus, and the SPA learns the real outcome once the
/// read model (GL-23) catches up.
///
/// Every <c>OwnerId</c>/<c>RequesterId</c> comes from <see cref="ServerCallContextExtensions.RequireUserId"/>
/// — the validated JWT — never from the wire message: the proto carries no such field at all
/// (see giftlists.proto's own remarks), so there is no client-populatable field to trust by
/// mistake, and GiftLists' handlers trust whatever id a command carries with no ownership check
/// of their own downstream.
///
/// Mapped through <c>GatewayGrpcEndpointRouteBuilderExtensions.MapGatewayGrpcServices</c> with
/// <c>RequireAuthorization()</c> — unlike <c>AuthGrpcService</c>, which must stay anonymous
/// (SignUp/Login are how a caller gets a token in the first place), every RPC here needs one
/// already.
///
/// Every string id off the wire (<c>list_id</c>/<c>item_id</c>) goes through
/// <see cref="ParseId"/> rather than a bare <see cref="Guid.Parse(string)"/> (GL-71 Batch 14
/// review): an empty or malformed id previously escaped as an unhandled
/// <see cref="FormatException"/>, which Grpc.Core surfaces to the browser as a bare
/// <c>Unknown</c> status with no <see cref="ErrorToRpcExceptionMapper.ErrorCodeTrailerName"/>
/// trailer — not a row in <c>ErrorKindTransportMapping</c>'s table at all, and exactly the
/// generic-500 shape Phase 1's exit criteria rule out. <see cref="GiftListErrors.InvalidId"/>
/// already exists for this (its own doc comment: "shared across every request field that is a
/// required id"), so this reuses it rather than inventing a second code for the same semantic.
/// </summary>
internal sealed class GiftListsGrpcService(IBus bus) : GiftListsService.GiftListsServiceBase
{
    public override async Task<CreateGiftListResponse> CreateGiftList(CreateGiftListRequest request, ServerCallContext context)
    {
        var ownerId = context.RequireUserId();
        // proto3 message fields carry presence but cannot be marked required — an omitted
        // ExpiresAt deserializes to null, not a default Timestamp (GL-71 Batch 14 review,
        // confirmed over the real wire: new CreateGiftListRequest().ExpiresAt is null), so this
        // guard is load-bearing, not defensive.
        if (request.ExpiresAt is null)
        {
            throw GiftListErrors.MissingExpiry.ToRpcException();
        }

        var listId = Guid.NewGuid();
        var command = new CreateGiftList(listId, ownerId, request.Name, request.ExpiresAt.ToDateTimeOffset());
        await bus.Send(command);

        return new CreateGiftListResponse { ListId = listId.ToString() };
    }

    public override async Task<RenameGiftListResponse> RenameGiftList(RenameGiftListRequest request, ServerCallContext context)
    {
        var requesterId = context.RequireUserId();
        var command = new RenameGiftList(ParseId(request.ListId), requesterId, request.Name);
        await bus.Send(command);

        return new RenameGiftListResponse();
    }

    public override async Task<DeleteGiftListResponse> DeleteGiftList(DeleteGiftListRequest request, ServerCallContext context)
    {
        var requesterId = context.RequireUserId();
        var command = new DeleteGiftList(ParseId(request.ListId), requesterId);
        await bus.Send(command);

        return new DeleteGiftListResponse();
    }

    public override async Task<AddGiftItemResponse> AddGiftItem(AddGiftItemRequest request, ServerCallContext context)
    {
        var requesterId = context.RequireUserId();
        var itemId = Guid.NewGuid();
        var command = new AddGiftItem(
            ParseId(request.ListId),
            requesterId,
            itemId,
            request.Name,
            request.HasDescription ? request.Description : null,
            request.HasUrl ? request.Url : null);
        await bus.Send(command);

        return new AddGiftItemResponse { ItemId = itemId.ToString() };
    }

    public override async Task<RemoveGiftItemResponse> RemoveGiftItem(RemoveGiftItemRequest request, ServerCallContext context)
    {
        var requesterId = context.RequireUserId();
        var command = new RemoveGiftItem(ParseId(request.ListId), requesterId, ParseId(request.ItemId));
        await bus.Send(command);

        return new RemoveGiftItemResponse();
    }

    /// <summary>
    /// <see cref="Guid.Parse(string)"/> throws a bare, unmapped <see cref="FormatException"/> on
    /// an empty or malformed id — this turns that into the same <c>gateway.invalid_id</c> /
    /// <see cref="Grpc.Core.StatusCode.InvalidArgument"/> shape every other validation failure in
    /// this codebase uses (CONVENTIONS.md "Errors"), rather than the unmapped <c>Unknown</c> status a
    /// raw exception surfaces as.
    /// </summary>
    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out var id) ? id : throw GiftListErrors.InvalidId.ToRpcException();
}
