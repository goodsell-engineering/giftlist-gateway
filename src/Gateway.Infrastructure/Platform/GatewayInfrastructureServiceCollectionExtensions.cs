using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Gateway.Application.Common;
using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.GetMyGiftLists;
using Gateway.Application.GiftLists.RecordGiftItemAdded;
using Gateway.Application.GiftLists.RecordGiftItemRemoved;
using Gateway.Application.GiftLists.RecordGiftListCreated;
using Gateway.Application.GiftLists.RecordGiftListDeleted;
using Gateway.Application.GiftLists.RecordGiftListRenamed;
using Gateway.Application.GiftLists.ViewGiftList;
using Gateway.Application.Reservations;
using Gateway.Application.Reservations.RecordGiftReserved;
using Gateway.Infrastructure.GiftLists.GraphQL;
using Gateway.Infrastructure.GiftLists.Messaging;
using Gateway.Infrastructure.GiftLists.Persistence;
using Gateway.Infrastructure.Platform.Security;
using Gateway.Infrastructure.Reservations.GraphQL;
using Gateway.Infrastructure.Reservations.Messaging;
using Gateway.Infrastructure.Reservations.Persistence;
using GiftLists.Contracts.GiftLists.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Rebus.Bus;
using Rebus.Config;
using Reservations.Contracts.Reservations.Events;

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// Gateway's composition root, called once from <c>Gateway.Host</c>'s <c>Program.cs</c>. Host
/// itself contains no wiring beyond the call to this method (CONVENTIONS.md "Project reference graph"); everything below
/// is grouped by domain (<c>GiftLists/...</c>) rather than by technical category, same as
/// production code (CONVENTIONS.md "Folder structure") — <c>Platform/</c> holds only this aggregator and the
/// framework-wiring pieces (auth, logging decorator) that belong to no single domain.
/// </summary>
public static class GatewayInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddGrpc();
        AddGiftListProjection(services);
        AddReservationProjection(services);
        AddGraphQl(services, environment);
        AddAuthentication(services, configuration);
        AddRateLimiting(services);
        return services;
    }

    /// <summary>
    /// The indexes the read models' queries rely on (ARCHITECTURE.md "Data model") — a
    /// correctness requirement, applied once at startup rather than left to be inferred from
    /// application code, mirroring <c>GiftListsInfrastructureServiceCollectionExtensions.EnsureIndexesAsync</c>.
    /// </summary>
    public static async Task EnsureIndexesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var database = serviceProvider.GetRequiredService<IMongoDatabase>();
        await GiftListProjectionRepository.EnsureIndexesAsync(database, cancellationToken);
        await ReservationProjectionRepository.EnsureIndexesAsync(database, cancellationToken);
    }

    /// <summary>
    /// Subscribes to every upstream integration event this service's read models are built from —
    /// GiftLists' five (GL-23) and Reservations' one (GL-38). Called once from <c>Program.cs</c>,
    /// after the host is built — mirrors <see cref="EnsureIndexesAsync"/>'s own placement/rationale.
    /// Public, and named after the wire events only in its doc comment rather than its signature,
    /// for the same reason <c>GatewayMessageRouting</c> exists at all: Host may not reference
    /// another service's Contracts directly (CONVENTIONS.md "Project reference graph"), only call
    /// through to Infrastructure, which may.
    /// </summary>
    public static async Task SubscribeToUpstreamEventsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var bus = serviceProvider.GetRequiredService<IBus>();
        await bus.Subscribe<GiftListCreatedV1>();
        await bus.Subscribe<GiftListRenamedV1>();
        await bus.Subscribe<GiftListDeletedV1>();
        await bus.Subscribe<GiftItemAddedV1>();
        await bus.Subscribe<GiftItemRemovedV1>();
        await bus.Subscribe<GiftReservedV1>();
    }

    private static void AddGiftListProjection(IServiceCollection services)
    {
        services.AddScoped<IGiftListProjectionRepository, GiftListProjectionRepository>();

        services.AddScoped<IValidator<RecordGiftListCreatedRequest>, RecordGiftListCreatedValidator>();
        services.AddScoped<IInteractor<RecordGiftListCreatedRequest, RecordGiftListCreatedResponse>, RecordGiftListCreatedInteractor>();

        services.AddScoped<IValidator<RecordGiftListRenamedRequest>, RecordGiftListRenamedValidator>();
        services.AddScoped<IInteractor<RecordGiftListRenamedRequest, RecordGiftListRenamedResponse>, RecordGiftListRenamedInteractor>();

        services.AddScoped<IValidator<RecordGiftListDeletedRequest>, RecordGiftListDeletedValidator>();
        services.AddScoped<IInteractor<RecordGiftListDeletedRequest, RecordGiftListDeletedResponse>, RecordGiftListDeletedInteractor>();

        services.AddScoped<IValidator<RecordGiftItemAddedRequest>, RecordGiftItemAddedValidator>();
        services.AddScoped<IInteractor<RecordGiftItemAddedRequest, RecordGiftItemAddedResponse>, RecordGiftItemAddedInteractor>();

        services.AddScoped<IValidator<RecordGiftItemRemovedRequest>, RecordGiftItemRemovedValidator>();
        services.AddScoped<IInteractor<RecordGiftItemRemovedRequest, RecordGiftItemRemovedResponse>, RecordGiftItemRemovedInteractor>();

        services.AddScoped<IValidator<GetMyGiftListsRequest>, GetMyGiftListsValidator>();
        services.AddScoped<IInteractor<GetMyGiftListsRequest, GetMyGiftListsResponse>, GetMyGiftListsInteractor>();

        // GL-38: one interactor behind giftList(id), sharedGiftList(token) and the subscription —
        // the only type that holds IReservationProjectionRepository (ARCHITECTURE.md "Defence in
        // depth on the owner-facing path"; pinned by ReservationProjectionPortIsolationTests).
        services.AddScoped<IValidator<ViewGiftListRequest>, ViewGiftListValidator>();
        services.AddScoped<IInteractor<ViewGiftListRequest, ViewGiftListResponse>, ViewGiftListInteractor>();

        services.AddRebusHandler<GiftListCreatedV1Handler>();
        services.AddRebusHandler<GiftListRenamedV1Handler>();
        services.AddRebusHandler<GiftListDeletedV1Handler>();
        services.AddRebusHandler<GiftItemAddedV1Handler>();
        services.AddRebusHandler<GiftItemRemovedV1Handler>();
    }

    /// <summary>
    /// GL-38: the reservation projection — a second collection, its own two ports (read for
    /// ViewGiftList, write for the GiftReservedV1 path — one class behind both, see
    /// <see cref="ReservationProjectionRepository"/>), and the notifier that turns a projected
    /// event into a push on the share-token-scoped subscription (ARCHITECTURE.md "Realtime
    /// updates").
    /// </summary>
    private static void AddReservationProjection(IServiceCollection services)
    {
        services.AddScoped<IReservationProjectionRepository, ReservationProjectionRepository>();
        services.AddScoped<IReservationProjectionWriter, ReservationProjectionRepository>();
        services.AddScoped<IReservationChangeNotifier, ReservationChangeNotifier>();

        services.AddScoped<IValidator<RecordGiftReservedRequest>, RecordGiftReservedValidator>();
        services.AddScoped<IInteractor<RecordGiftReservedRequest, RecordGiftReservedResponse>, RecordGiftReservedInteractor>();

        services.AddRebusHandler<GiftReservedV1Handler>();
    }

    /// <summary>
    /// The decorator pair and the GraphQL server, after every interactor across every domain has
    /// been registered — Scrutor's Decorate wraps what is already in the collection, so this must
    /// run last. One open-generic decorator pair, applied to every IInteractor&lt;,&gt; — Validation,
    /// then Logging, in that order in every service (CONVENTIONS.md "Use cases").
    /// </summary>
    private static void AddGraphQl(IServiceCollection services, IHostEnvironment environment)
    {
        services.Decorate(typeof(IInteractor<,>), typeof(Validating<,>));
        services.Decorate(typeof(IInteractor<,>), typeof(Logging<,>));

        services
            .AddGraphQLServer()
            .AddQueryType<GiftListQueries>()
            // GL-38: the one subscription, share-token scoped; in-memory topics, so one Gateway
            // instance (ARCHITECTURE.md "Realtime updates").
            .AddSubscriptionType<GiftListSubscriptions>()
            .AddInMemorySubscriptions()
            // GL-44: introspection is part of the same "who gets to see this schema" policy as
            // the IDE and `?sdl` (GatewayGraphQlEndpointRouteBuilderExtensions.
            // MapGatewayGraphQlEndpoints, where the other two are set and the decision is written
            // up) — kept here instead because HotChocolate.Types' DisableIntrospection is an
            // IRequestExecutorBuilder concern (a document-validation rule baked into the schema at
            // build time), not a GraphQLServerOptions transport switch the endpoint mapping can
            // reach. Development allows it (any client library that introspects a schema to
            // generate its own types needs it); every other environment does not.
            .DisableIntrospection(!environment.IsDevelopment());
    }

    /// <summary>
    /// GL-44: rate limiting on the Gateway's two no-JWT edges — <c>AuthGrpcService</c>
    /// (SignUp/Login) and <c>ReservationsGrpcService</c> (ReserveGift, the share-token guest
    /// surface) — applied per grpc-web endpoint in <c>GatewayGrpcEndpointRouteBuilderExtensions</c>
    /// via <see cref="GatewayRateLimitPolicies"/>. <c>GiftListsGrpcService</c> carries a JWT on
    /// every call and is deliberately left unlimited here (its own <c>RequireAuthorization()</c>
    /// is the control that matters for it); a signed-in caller could be partitioned by user id
    /// instead of IP if this were ever extended to it, but neither of these two endpoints ever
    /// sees a JWT, so IP address (<see cref="ClientIp"/>) is what's left to partition by — a
    /// share-token guest and a not-yet-registered sign-up both look the same to this service:
    /// an anonymous caller from one address.
    /// </summary>
    /// <remarks>
    /// Known limitation, accepted rather than solved here: a rejected request gets a plain HTTP
    /// 429, not a grpc <c>RESOURCE_EXHAUSTED</c> status — <c>Microsoft.AspNetCore.RateLimiting</c>
    /// operates below grpc-web's own status/trailer framing (the same layering problem
    /// <c>Program.cs</c>'s CORS comment describes for exposed headers), and wiring a proper grpc
    /// status through would mean a rate-limiting grpc <c>Interceptor</c> instead of this
    /// middleware. Good enough for this demo; flagged rather than silently accepted.
    /// </remarks>
    private static void AddRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // 5/minute/IP: generous enough for a real caller who mistypes a password or retries a
            // sign-up once or twice, tight enough to blunt scripted credential stuffing / sign-up
            // spam against a demo with no CAPTCHA and no email verification step.
            options.AddPolicy(GatewayRateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // 20/minute/IP: a guest genuinely working down a gift list reserving several items in
            // a few minutes is ordinary traffic; this is sized to stop a scripted sweep of every
            // item on a list (or across several lists reached through one token/IP), not to
            // throttle a person.
            options.AddPolicy(GatewayRateLimitPolicies.Reservation, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
    }

    /// <summary>
    /// No forwarded-header handling (no reverse proxy sits in front of this Gateway in devenv) —
    /// the direct TCP peer address is the whole story here, same as it would be for any other
    /// per-IP concern this service might grow. Revisit if a proxy is ever introduced in front of
    /// it (CONVENTIONS.md has no rule for this yet; it would need `ForwardedHeadersMiddleware`,
    /// applied before this reads <see cref="HttpContext.Connection"/>, not after).
    /// </summary>
    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Validates the JWT the SPA sends with every GraphQL request against Identity's public
    /// signing key (ARCHITECTURE.md "Why workers still need a little HTTP", "Auth & sharing"). Fails fast at startup on missing config, the same
    /// way <c>AddBuildingBlocksMongo</c>/<c>AddBuildingBlocksRebus</c> do — a Gateway that cannot
    /// validate a token should never come up looking healthy.
    /// </summary>
    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GatewayJwtOptions.ConfigurationSection);
        var publicKeyPem = section[nameof(GatewayJwtOptions.PublicKeyPem)];
        var issuer = section[nameof(GatewayJwtOptions.Issuer)];
        var audience = section[nameof(GatewayJwtOptions.Audience)];

        if (string.IsNullOrWhiteSpace(publicKeyPem) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException(
                "Missing required configuration value(s) under 'Jwt' ('Jwt:PublicKeyPem', " +
                "'Jwt:Issuer', 'Jwt:Audience' — set via 'Jwt__PublicKeyPem' etc. environment " +
                "variables). Mirrors Identity's own 'Jwt:SigningKeyPem' fail-fast; the Gateway " +
                "holds only the public half of that same key pair.");
        }

        // devenv's env files carry a multi-line PEM with literal '\n' escapes (the usual trick
        // for a KEY=VALUE env line) — same un-escaping JwtTokenIssuer does on Identity's side.
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Without this, the JwtSecurityTokenHandler's legacy inbound claim map silently
                // rewrites the token's own "sub" claim type to the long
                // http://schemas.xmlsoap.org/.../nameidentifier XML-namespace URI, so
                // HttpContextExtensions.RequireUserId's FindFirst(JwtRegisteredClaimNames.Sub) —
                // looking for the literal "sub" JwtTokenIssuer actually wrote — never finds it
                // and every request looks unauthenticated regardless of a perfectly valid token
                // (found the hard way, GL-23 review).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(rsa),
                    ValidateLifetime = true,
                };
            });
        services.AddAuthorization();
    }
}
