using System.IdentityModel.Tokens.Jwt;
using BuildingBlocks.Results;
using Gateway.Infrastructure.Platform.Transport;
using Grpc.AspNetCore.Server;
using Grpc.Core;
// Aliased — this project also carries HotChocolate's implicit global `using HotChocolate;`, and
// HotChocolate.Error collides with BuildingBlocks.Results.Error (CS0104), same as
// ErrorToRpcExceptionMapper/ErrorToGraphQlErrorMapper.
using Error = BuildingBlocks.Results.Error;

namespace Gateway.Infrastructure.Platform.Security;

/// <summary>
/// The <see cref="ServerCallContext"/> (grpc-web) counterpart of
/// <see cref="HttpContextExtensions.RequireUserId(Microsoft.AspNetCore.Http.HttpContext)"/> —
/// reads the caller's own id off the validated JWT (ARCHITECTURE.md "Auth & sharing"), the one thing every
/// owner/requester-scoped gRPC method needs before it can build a GiftLists command (GL-71).
/// <see cref="ServerCallContext.GetHttpContext"/> is the one hop between the two: grpc-web still
/// runs on top of the ordinary ASP.NET Core pipeline, so <c>app.UseAuthentication()</c> has
/// already populated the same <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> a GraphQL
/// resolver reads.
///
/// Deliberately not a <see cref="Result{T}"/>-returning method, for the same reason
/// <c>HttpContextExtensions.RequireUserId</c> is not one: whether the caller is who they claim to
/// be is a transport-level authentication fact, not a use-case outcome. Every RPC that calls this
/// is mapped with <c>RequireAuthorization()</c> (<c>GatewayGrpcEndpointRouteBuilderExtensions</c>),
/// so in practice the token is already known-valid by the time a handler runs this — this is the
/// one place that turns a missing/unparseable <c>sub</c> claim into the same
/// <c>RpcException</c> shape every other Gateway failure uses, rather than an unrelated crash.
/// </summary>
internal static class ServerCallContextExtensions
{
    private static readonly Error Unauthenticated = new(
        "gateway.unauthenticated", "A valid access token is required.", ErrorKind.Unauthenticated);

    public static Guid RequireUserId(this ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.GetHttpContext().User;
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(subject, out var userId))
        {
            throw Unauthenticated.ToRpcException();
        }

        return userId;
    }
}
