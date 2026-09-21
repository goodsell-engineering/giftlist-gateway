using BuildingBlocks.Messaging.RequestReply;
using Gateway.Application.GiftLists;
using Gateway.Infrastructure.Platform.Transport;
using Grpc.Core;

namespace Gateway.Infrastructure.Reservations.Grpc;

/// <summary>
/// The browser's grpc-web entry point for reserving a gift (GL-37) — the guest surface, reached
/// with a share token and an item id and nothing else (reservations.proto's own remarks: there is
/// no list-id field on the wire at all). Mirrors <c>AuthGrpcService</c>'s <c>SignUp</c>/<c>Login</c>
/// shape exactly: thin translation, no local use case (the Gateway mutates no aggregate of its
/// own here — the Reservations aggregate is Reservations' to mutate, not this service's), await
/// the reply through <see cref="IRequestReplyBridge"/>, translate the <see cref="BuildingBlocks.Results.Result{T}"/>
/// back into a wire response or an <see cref="RpcException"/>.
/// </summary>
/// <remarks>
/// <para>
/// The share token is the whole credential — resolved to a list id through the Gateway's own
/// read model (<see cref="IGiftListProjectionRepository.FindByShareTokenAsync"/>, the same lookup
/// <c>ViewGiftListInteractor</c>'s guest branch uses), never a client-supplied list id (there is
/// none to supply). A token that fails <see cref="ShareTokenFormat"/>'s shape check is
/// <see cref="GiftListErrors.InvalidShareToken"/>, the same code and reasoning as
/// <c>ViewGiftListValidator</c>'s; one that is well-formed but resolves to nothing (unknown, or a
/// list since deleted) is <see cref="GiftListErrors.NotFound"/>, mirroring
/// <c>ViewGiftListInteractor</c>'s guest branch again — a code names a semantic, not the place it
/// was raised (CONVENTIONS.md "Errors").
/// </para>
/// <para>
/// Once the list id is known, this is a pure proxy to Reservations' own <c>ReserveGift</c>
/// command — every eligibility check (does the list exist on Reservations' own copy, is it
/// deleted or expired, does the item exist on it, has it already been reserved) belongs to
/// Reservations' <c>ReserveGiftInteractor</c>, not duplicated here. <see cref="Error.ToRpcException"/>
/// maps whichever <c>reservation.*</c> code comes back generically, by <see cref="Error.Kind"/> —
/// the same table (<see cref="ErrorToRpcExceptionMapper"/>) every other Gateway grpc-web endpoint
/// uses, so no new per-code switch is needed here for
/// <c>giftlist_not_found</c>/<c>giftlist_deleted</c>/<c>giftlist_expired</c>/<c>giftitem_not_found</c>/
/// <c>already_reserved</c>/<c>invalid_id</c> to travel correctly.
/// </para>
/// <para>
/// The item id is parsed the same way <c>GiftListsGrpcService.ParseId</c> parses every other
/// caller-supplied id: a malformed (non-Guid) string is rejected here as
/// <see cref="GiftListErrors.InvalidId"/>, but <see cref="Guid.Empty"/> is deliberately let
/// through to Reservations rather than pre-empted — Reservations' own <c>ReserveGiftValidator</c>
/// already rejects an empty id as <c>reservation.invalid_id</c>, and letting that one specific
/// case reach the real service is what proves this mapper's generic <c>ErrorKind</c> translation
/// actually carries that code end to end, rather than assuming it from the identical-looking
/// Gateway-side code of the same name.
/// </para>
/// <para>
/// Maps anonymously (<c>GatewayGrpcEndpointRouteBuilderExtensions</c>) — like <c>AuthGrpcService</c>,
/// not <c>GiftListsGrpcService</c>: a guest holding nothing but a share link has no JWT to present,
/// and the share token is the credential this RPC actually checks.
/// </para>
/// </remarks>
internal sealed class ReservationsGrpcService(
    IGiftListProjectionRepository giftLists, IRequestReplyBridge bridge)
    : ReservationsService.ReservationsServiceBase
{
    public override async Task<ReserveGiftResponse> ReserveGift(ReserveGiftRequest request, ServerCallContext context)
    {
        if (!ShareTokenFormat.IsValid(request.ShareToken))
        {
            throw GiftListErrors.InvalidShareToken.ToRpcException();
        }

        var itemId = ParseId(request.ItemId);

        var list = await giftLists.FindByShareTokenAsync(request.ShareToken, context.CancellationToken);
        if (list is null)
        {
            throw GiftListErrors.NotFound.ToRpcException();
        }

        var command = new global::Reservations.Contracts.Reservations.ReserveGift(list.ListId, itemId);
        var result = await bridge.SendAndAwaitReply<global::Reservations.Contracts.Reservations.ReserveGiftReply>(
            command, context.CancellationToken);

        return result.Match(
            onSuccess: reply => new ReserveGiftResponse { ReleaseSecret = reply.ReleaseSecret },
            onFailure: error => throw error.ToRpcException());
    }

    /// <summary>See <see cref="GiftListsGrpcService.ParseId"/>'s doc comment — same rationale, same reused code, this file's own remarks explain why an empty (but well-formed) Guid is deliberately not caught here as well.</summary>
    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out var id) ? id : throw GiftListErrors.InvalidId.ToRpcException();
}
