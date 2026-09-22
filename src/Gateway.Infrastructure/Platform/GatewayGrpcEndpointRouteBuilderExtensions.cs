using Gateway.Infrastructure.GiftLists.Grpc;
using Gateway.Infrastructure.Reservations.Grpc;
using Gateway.Infrastructure.Users.Grpc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// Maps every grpc-web endpoint the Gateway exposes. Kept here rather than inline in
/// <c>Program.cs</c> so <see cref="AuthGrpcService"/>/<see cref="GiftListsGrpcService"/>/
/// <see cref="ReservationsGrpcService"/> — like every Rebus handler in this codebase — can stay
/// <c>internal</c>: Host never needs to name the concrete service type, only call this one
/// extension (mirrors <c>Identity.Infrastructure</c> keeping its handlers internal behind
/// <c>AddIdentityInfrastructure</c>'s <c>AddRebusHandler&lt;T&gt;</c> calls).
///
/// <see cref="AuthGrpcService"/> and <see cref="ReservationsGrpcService"/> stay anonymous
/// (SignUp/Login are how a caller gets a token in the first place, identity.proto's own remarks;
/// a guest reserving through a share link has no JWT to present at all, and the share token
/// itself is the credential <see cref="ReservationsGrpcService"/> checks — GL-37);
/// <see cref="GiftListsGrpcService"/> requires one via <c>RequireAuthorization()</c> (GL-71) —
/// <c>Program.cs</c> calls <c>app.UseAuthorization()</c> with no default policy, so
/// authentication is not enforced by default and has to be opted into per endpoint here, the same
/// way <c>[Authorize]</c> would on an MVC controller.
///
/// GL-44: <see cref="AuthGrpcService"/> and <see cref="ReservationsGrpcService"/> — the two
/// no-JWT edges above, exactly — also carry <c>RequireRateLimiting</c>, one policy per service
/// (<see cref="GatewayRateLimitPolicies"/>; the limits and the partition key are decided and
/// documented in <c>GatewayInfrastructureServiceCollectionExtensions.AddRateLimiting</c>).
/// <see cref="GiftListsGrpcService"/> carries no policy — its <c>RequireAuthorization()</c> is
/// the control that matters for an authenticated caller; see that method's own remarks for why
/// rate limiting was not extended to it here.
/// </summary>
public static class GatewayGrpcEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapGatewayGrpcServices(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<AuthGrpcService>().RequireRateLimiting(GatewayRateLimitPolicies.Auth);
        endpoints.MapGrpcService<GiftListsGrpcService>().RequireAuthorization();
        endpoints.MapGrpcService<ReservationsGrpcService>().RequireRateLimiting(GatewayRateLimitPolicies.Reservation);
        return endpoints;
    }
}
