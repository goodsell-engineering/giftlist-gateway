using BuildingBlocks.Messaging.RequestReply;
using Gateway.Infrastructure.Platform.Transport;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Identity.Contracts.Users;

namespace Gateway.Infrastructure.Users.Grpc;

/// <summary>
/// The browser's grpc-web entry point for sign-up and log-in (GL-18). Thin by design, the same
/// way a Rebus handler is thin (CONVENTIONS.md "Messaging"): translate the wire request into Identity's
/// command, await the reply through <see cref="IRequestReplyBridge"/> — never call Identity
/// directly — and translate the <see cref="Result{T}"/> back into a wire response or an
/// <see cref="RpcException"/>. There is no local use case here: the Gateway has no aggregate of
/// its own to mutate for either operation, so this is pure protocol translation, an Infrastructure
/// adapter concern, not an Application-ring interactor.
/// </summary>
internal sealed class AuthGrpcService(IRequestReplyBridge bridge) : AuthService.AuthServiceBase
{
    public override async Task<SignUpResponse> SignUp(SignUpRequest request, ServerCallContext context)
    {
        var command = new SignUp(request.Email, request.Password, request.DisplayName);
        var result = await bridge.SendAndAwaitReply<SignUpReply>(command, context.CancellationToken);

        return result.Match(
            onSuccess: ToResponse,
            onFailure: error => throw error.ToRpcException());
    }

    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        var command = new Login(request.Email, request.Password);
        var result = await bridge.SendAndAwaitReply<LoginReply>(command, context.CancellationToken);

        return result.Match(
            onSuccess: ToResponse,
            onFailure: error => throw error.ToRpcException());
    }

    private static SignUpResponse ToResponse(SignUpReply reply) => new()
    {
        UserId = reply.UserId.ToString(),
        AccessToken = reply.AccessToken,
        AccessTokenExpiresAt = Timestamp.FromDateTimeOffset(reply.AccessTokenExpiresAt),
    };

    private static LoginResponse ToResponse(LoginReply reply) => new()
    {
        UserId = reply.UserId.ToString(),
        AccessToken = reply.AccessToken,
        AccessTokenExpiresAt = Timestamp.FromDateTimeOffset(reply.AccessTokenExpiresAt),
    };
}
