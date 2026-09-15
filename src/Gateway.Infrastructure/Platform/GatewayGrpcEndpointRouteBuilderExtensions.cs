using Gateway.Infrastructure.GiftLists.Grpc;
using Gateway.Infrastructure.Users.Grpc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// Maps every grpc-web endpoint the Gateway exposes. Kept here rather than inline in
/// <c>Program.cs</c> so <see cref="AuthGrpcService"/>/<see cref="GiftListsGrpcService"/> — like
/// every Rebus handler in this codebase — can stay <c>internal</c>: Host never needs to name the
/// concrete service type, only call this one extension (mirrors <c>Identity.Infrastructure</c>
/// keeping its handlers internal behind <c>AddIdentityInfrastructure</c>'s
/// <c>AddRebusHandler&lt;T&gt;</c> calls).
///
/// <see cref="AuthGrpcService"/> stays anonymous (SignUp/Login are how a caller gets a token in
/// the first place, identity.proto's own remarks); <see cref="GiftListsGrpcService"/> requires
/// one via <c>RequireAuthorization()</c> (GL-71) — <c>Program.cs</c> calls
/// <c>app.UseAuthorization()</c> with no default policy, so authentication is not enforced by
/// default and has to be opted into per endpoint here, the same way <c>[Authorize]</c> would on
/// an MVC controller.
/// </summary>
public static class GatewayGrpcEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapGatewayGrpcServices(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<AuthGrpcService>();
        endpoints.MapGrpcService<GiftListsGrpcService>().RequireAuthorization();
        return endpoints;
    }
}
