using BuildingBlocks.HealthChecks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Gateway.Infrastructure.Platform;
using Gateway.Infrastructure.Platform.Transport;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// The Gateway's own read-model database and its Rebus input queue, used for command sends and
// the request/reply pattern (ARCHITECTURE.md "Command → event flow", "Data model", "Tech stack") — wiring these here, ahead of any
// use case, is what makes "healthy Rebus connection + Mongo connection" (Phase 0 exit
// criterion) an observable fact rather than something scraped out of logs.
builder.Services.AddBuildingBlocksMongo(builder.Configuration, "gateway");
// GatewayMessageRouting.Configure routes SignUp/Login to Identity's queue — kept out of this
// file because it names Identity.Contracts types, and CONVENTIONS.md "Project reference graph" only lets Infrastructure
// (not Host) reference another service's Contracts.
builder.Services.AddBuildingBlocksRebus(builder.Configuration, "gateway", GatewayMessageRouting.Configure);
builder.Services.AddBuildingBlocksHealthChecks(builder.Configuration);
builder.Services.AddGatewayInfrastructure(builder.Configuration);

// GL-18: the SPA talks grpc-web straight to the Gateway — there is no GraphQL/REST hop in front
// of it for Phase 1 auth. Browser and Gateway are different origins in dev (5173 vs 8080) and
// across environments, so CORS is real, not incidental; "Cors:AllowedOrigins" is unset (empty)
// by default, which fails closed rather than defaulting to "allow everything" in an environment
// nobody configured yet.
const string GatewayCorsPolicy = "Gateway";
builder.Services.AddCors(options =>
{
    options.AddPolicy(GatewayCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        // grpc-web's status/message travel as trailers exposed through these headers — without
        // this the browser's grpc-web client cannot read a failed call's status at all, and every
        // error looks like a generic network failure instead of ABORTED/UNAUTHENTICATED/etc.
        // ErrorCodeTrailerName is the one Gateway-specific addition (GL-18 review): without it
        // exposed too, the browser gets the gRPC status but never the Error.Code trailer that
        // distinguishes e.g. "still processing" from "genuinely unavailable".
        .WithExposedHeaders(
            "Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding",
            ErrorToRpcExceptionMapper.ErrorCodeTrailerName));
});

var app = builder.Build();

app.UseCors(GatewayCorsPolicy);

// GL-23: populates HttpContext.User from the SPA's JWT (if any) ahead of every request,
// including GraphQL's — myGiftLists/giftList(id) read the caller's id off it
// (HttpContextExtensions.RequireUserId). An absent/invalid token is not rejected here (that
// is a bare 401 with no GraphQL-shaped error body); each resolver turns that into a proper
// GraphQL error instead, the same way a failed use case does.
app.UseAuthentication();
app.UseAuthorization();

// Decodes grpc-web framing (HTTP/1.1 + base64/text-friendly) into ordinary gRPC before it
// reaches AuthGrpcService — applied to every mapped grpc service by default rather than opted in
// per endpoint, since Phase 1 has no non-grpc-web caller of this Gateway.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGatewayGrpcServices();
// GL-38: the share-token-scoped subscription rides a WebSocket (ARCHITECTURE.md "Realtime
// updates"); HotChocolate's endpoint upgrades the connection itself, but only if ASP.NET Core's
// WebSocket middleware is in the pipeline ahead of it.
app.UseWebSockets();
app.MapGatewayGraphQlEndpoints();

// GL-23/GL-38: the indexes the read models' queries rely on, and the subscriptions that let
// GiftLists' and Reservations' integration events actually reach this process — both correctness
// requirements, applied once at startup rather than left implicit (mirrors GiftLists.Host's own
// EnsureIndexesAsync call).
await GatewayInfrastructureServiceCollectionExtensions.EnsureIndexesAsync(app.Services, CancellationToken.None);
await GatewayInfrastructureServiceCollectionExtensions.SubscribeToUpstreamEventsAsync(app.Services, CancellationToken.None);

// Liveness: only "is the process up and answering HTTP". Deliberately checks nothing
// external — a RabbitMQ/Mongo blip must not make Docker kill an otherwise-healthy container
// (ARCHITECTURE.md "Why workers still need a little HTTP").
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: the Mongo + RabbitMQ checks BuildingBlocks registered above, tagged "ready".
// This is the endpoint compose's healthcheck targets, because it's the one that should gate
// `depends_on: condition: service_healthy` for `web`.
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

// Exposes the otherwise-internal top-level Program class to Gateway.IntegrationTests'
// WebApplicationFactory<Program> (CONVENTIONS.md "Testing": integration tests enter through the real
// host, not a re-assembled stand-in) — a no-op at runtime, purely a compile-time visibility hook.
public partial class Program;
