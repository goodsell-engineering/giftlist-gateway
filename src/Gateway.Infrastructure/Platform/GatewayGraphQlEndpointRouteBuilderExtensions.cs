using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// Maps the Gateway's one GraphQL endpoint (GL-23). Kept here, alongside
/// <see cref="GatewayGrpcEndpointRouteBuilderExtensions"/>, so <c>Gateway.Host</c>'s
/// <c>Program.cs</c> never needs to reference <c>HotChocolate.AspNetCore</c> by name
/// (CONVENTIONS.md "Project reference graph" — Host is wiring only, and the actual server registration
/// (<c>AddGraphQLServer</c>) lives in <see cref="GatewayInfrastructureServiceCollectionExtensions"/>).
/// </summary>
public static class GatewayGraphQlEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapGatewayGraphQlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGraphQL();
        return endpoints;
    }
}
