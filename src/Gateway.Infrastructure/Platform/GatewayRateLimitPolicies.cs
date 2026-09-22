namespace Gateway.Infrastructure.Platform;

/// <summary>
/// Names the rate-limit policies applied at the Gateway's edge (GL-44) — shared between
/// <see cref="GatewayInfrastructureServiceCollectionExtensions"/>, where they are defined, and
/// <see cref="GatewayGrpcEndpointRouteBuilderExtensions"/>, where they are applied, so the two
/// never drift apart as bare string literals would let them.
/// </summary>
internal static class GatewayRateLimitPolicies
{
    /// <summary><c>AuthGrpcService</c> — SignUp and Login, the whole of that grpc service.</summary>
    public const string Auth = "gateway.auth";

    /// <summary><c>ReservationsGrpcService</c> — ReserveGift, the share-token guest surface.</summary>
    public const string Reservation = "gateway.reservation";
}
