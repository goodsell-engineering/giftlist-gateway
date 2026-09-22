using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

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
    public static IEndpointRouteBuilder MapGatewayGraphQlEndpoints(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        // GL-105: HotChocolate's own defaults allow a query to be sent as a GET, with every
        // argument — including sharedGiftList's bearer-style `token` (ARCHITECTURE.md "Auth &
        // sharing") — in the URL's query string, where reverse-proxy access logs, browser
        // history and any URL-logging intermediary can read it. No caller needs this: the SPA's
        // GraphQL client always POSTs (giftlist-web's graphqlClient.ts). This leaves GET's other
        // job — serving the IDE at a bare GET /graphql — untouched, since that is a separate
        // code path (GraphQLServerOptions.Tool, left at its default) from GET-as-a-query-
        // transport (AllowedGetOperations).
        //
        // GL-44/GL-113: the IDE (Nitro, served at that same bare GET), `?sdl` (the whole schema
        // as one credential-free GET — GL-113's own finding) and introspection (a query's own
        // right to ask the schema about itself, set alongside AddGraphQLServer() in
        // GatewayInfrastructureServiceCollectionExtensions.AddGraphQl, since DisableIntrospection
        // is not a GraphQLServerOptions switch) are decided together, as one policy, rather than
        // left at HotChocolate's defaults — which serve all three, unconditionally, to anyone, in
        // every environment. None of the three is needed for the SPA, which only ever POSTs the
        // fixed operations giftlist-web itself defines. Development keeps all three (the tool a
        // developer actually reaches for, and the introspection any GraphQL client codegen needs
        // to work against this schema at all); every other environment turns all three off, so an
        // unauthenticated caller gets neither the IDE nor the schema, whole or by asking it
        // questions.
        //
        // WHICH ENVIRONMENT DEVENV ACTUALLY IS, since the policy above turns on it and an earlier
        // draft of this comment had it backwards: devenv's compose sets DOTNET_ENVIRONMENT:
        // Development in its x-dotnet-env anchor (docker-compose.yml, inherited by the gateway
        // block), and the host honours DOTNET_ENVIRONMENT when ASPNETCORE_ENVIRONMENT is unset —
        // which it is. SO THE DEMO STACK IS DEVELOPMENT AND SERVES ALL THREE, deliberately: it is
        // a laptop demo whose whole point is being explorable. What GL-113 asked for is that this
        // be chosen rather than inherited, and the choice is: on where a developer is the only
        // caller, off wherever the environment is anything else.
        var isDevelopment = environment.IsDevelopment();
        endpoints.MapGraphQL().WithOptions(options =>
        {
            options.AllowedGetOperations = AllowedGetOperations.None;
            options.Tool.Enable = isDevelopment;
            options.EnableSchemaRequests = isDevelopment;
        });
        return endpoints;
    }

    /// <summary>
    /// GL-109: guards against a different bug than <see cref="MapGatewayGraphQlEndpoints"/>'s own
    /// <c>AllowedGetOperations.None</c> already does. That setting answers a plain GET carrying
    /// `query=` with a clean 405 — but only when HotChocolate's content negotiation treats the
    /// request as GET-as-a-query-transport in the first place. When the request's Accept header
    /// prefers <c>text/html</c> instead — a browser address bar, not the SPA or a generated
    /// GraphQL client, neither of which ever sends that Accept header — HotChocolate instead
    /// reaches for the IDE-serving code path even though a `query` string parameter is present,
    /// and trips over it there: an unhandled exception that, absent this guard, reached the
    /// caller as a 500. Empirically (GL-109's own finding, read from the code rather than run,
    /// since this repo's Program.cs calls neither <c>UseDeveloperExceptionPage</c> nor
    /// <c>UseExceptionHandler</c> itself) that renders as a full stack-trace developer exception
    /// page in Development — <c>WebApplication.Build()</c> auto-registers that middleware itself,
    /// but only when the environment is Development — and as a bare, empty-bodied 500 everywhere
    /// else. Note which of those devenv got: its compose sets <c>DOTNET_ENVIRONMENT:
    /// Development</c> (honoured when <c>ASPNETCORE_ENVIRONMENT</c> is unset, and it is), so the
    /// demo stack served the FULL STACK TRACE, not the bare 500. The bare 500 leaks nothing but is
    /// no more deliberate — GL-105 already gives an API client a
    /// clean 405 for this exact shape of request, and a browser deserves the same one, in every
    /// environment. Short-circuiting here, ahead of <c>MapGraphQL()</c> and independent of the
    /// Accept header entirely, is what gets there without depending on which internal branch
    /// HotChocolate's content negotiation happens to take.
    /// </summary>
    public static IApplicationBuilder UseGatewayGraphQlGetQueryGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method)
                && context.Request.Path.StartsWithSegments("/graphql", StringComparison.OrdinalIgnoreCase)
                && context.Request.Query.ContainsKey("query"))
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }

            await next(context);
        });
}
