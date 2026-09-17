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
        // GL-105: HotChocolate's own defaults allow a query to be sent as a GET, with every
        // argument — including sharedGiftList's bearer-style `token` (ARCHITECTURE.md "Auth &
        // sharing") — in the URL's query string, where reverse-proxy access logs, browser
        // history and any URL-logging intermediary can read it. No caller needs this: the SPA's
        // GraphQL client always POSTs (giftlist-web's graphqlClient.ts). This leaves GET's other
        // job — serving the IDE at a bare GET /graphql — untouched, since that is a separate
        // code path (GraphQLServerOptions.Tool, left at its default) from GET-as-a-query-
        // transport (AllowedGetOperations).
        endpoints.MapGraphQL().WithOptions(options =>
        {
            options.AllowedGetOperations = AllowedGetOperations.None;
        });
        return endpoints;
    }
}
