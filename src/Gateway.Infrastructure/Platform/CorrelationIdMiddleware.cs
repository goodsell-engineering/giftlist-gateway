using BuildingBlocks.Messaging.CorrelationId;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// GL-45: the ingress that seeds a correlation id for every conversation this Gateway starts —
/// the half of ARCHITECTURE.md "Cross-cutting concerns" that was missing entirely before this: the
/// Rebus message→message hop was already wired (<c>RebusConfigurationExtensions.EnableCorrelationIdPropagation</c>,
/// applied identically in every service), but nothing established the FIRST id a gRPC/grpc-web
/// call or a GraphQL request carries, so <c>CorrelationIdOutgoingStep</c> always found the ambient
/// value <see langword="null"/> and stamped nothing on a Gateway-initiated <c>bus.Send</c>.
///
/// One piece of ASP.NET Core middleware covers both surfaces named in the issue (grpc-web and
/// GraphQL) rather than a grpc <c>Interceptor</c> plus separate HTTP middleware: grpc-web (like
/// plain gRPC hosted by Grpc.AspNetCore) rides ordinary HTTP/1.1+/2 requests through this same
/// pipeline, so a request-scoped middleware sees every inbound call regardless of which endpoint
/// eventually handles it — one place, not two, per the same "propagate it there once" principle
/// the Rebus step already follows.
///
/// Placed early in <c>Program.cs</c>'s pipeline (after CORS, before authentication/rate limiting/
/// grpc-web/GraphQL) so the scope is open for the whole request, including whatever those later
/// stages themselves log.
///
/// Public, unlike <see cref="Gateway.Infrastructure.Users.Grpc.AuthGrpcService"/> and friends —
/// mirrors <see cref="Transport.ErrorToRpcExceptionMapper"/>'s own reasoning: two other things
/// outside this assembly need <see cref="HeaderName"/> by name rather than a re-typed copy of the
/// same string literal, namely <c>Gateway.Host</c>'s CORS policy (<c>WithExposedHeaders</c> must
/// list it or a browser cannot read the echoed response header) and
/// <c>Gateway.IntegrationTests</c> (asserting the same id actually round-trips end to end).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>
    /// A caller-supplied value under this header is honoured as-is (so an operator or another
    /// service fronting this Gateway can hand it a correlation id to continue); otherwise one is
    /// generated. <c>X-Correlation-Id</c> is the conventional choice, and the Gateway's own CORS
    /// policy (<c>Program.cs</c>) already allows any request header (<c>AllowAnyHeader()</c>), so
    /// no CORS change was needed to let a browser caller send it — only to let one read the
    /// echoed response header back, which is the one thing <c>WithExposedHeaders</c> had to grow.
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(
        HttpContext context,
        ICorrelationIdAccessor accessor,
        ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = ResolveCorrelationId(context);

        // Queued rather than set immediately: ASP.NET Core forbids writing response headers once
        // the body has started (a real risk here — grpc-web and GraphQL subscriptions both stream),
        // and OnStarting always fires exactly once, right before the first byte goes out, whichever
        // branch of the pipeline produced it.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (accessor.BeginScope(correlationId))
        using (CorrelationIdLogging.BeginScope(logger, correlationId))
        {
            // Mirrors CorrelationIdIncomingStep's own log line on the Rebus side — the same
            // "one guaranteed log line naming the id" shape, so a test (or an operator) has
            // somewhere to find it in the formatted message text, not only in scope properties
            // BuildingBlocks.Testing's LogCapture cannot see (GL-117).
            logger.LogInformation(
                "Handling {Method} {Path} with correlation id {CorrelationId}.",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue(HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : Guid.NewGuid().ToString();
}

/// <summary>Hides <see cref="IApplicationBuilder.UseMiddleware{TMiddleware}"/>'s type-argument spelling from <c>Gateway.Host</c>'s <c>Program.cs</c> — mirrors <c>GatewayGraphQlEndpointRouteBuilderExtensions.UseGatewayGraphQlGetQueryGuard</c>'s own shape.</summary>
public static class GatewayCorrelationIdApplicationBuilderExtensions
{
    public static IApplicationBuilder UseGatewayCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
