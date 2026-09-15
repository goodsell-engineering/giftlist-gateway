using System.Security.Cryptography;
using Gateway.Application.Common;
using Gateway.Application.GiftLists;
using Gateway.Application.GiftLists.GetGiftList;
using Gateway.Application.GiftLists.GetMyGiftLists;
using Gateway.Application.GiftLists.RecordGiftItemAdded;
using Gateway.Application.GiftLists.RecordGiftItemRemoved;
using Gateway.Application.GiftLists.RecordGiftListCreated;
using Gateway.Application.GiftLists.RecordGiftListDeleted;
using Gateway.Application.GiftLists.RecordGiftListRenamed;
using Gateway.Infrastructure.GiftLists.GraphQL;
using Gateway.Infrastructure.GiftLists.Messaging;
using Gateway.Infrastructure.GiftLists.Persistence;
using Gateway.Infrastructure.Platform.Security;
using GiftLists.Contracts.GiftLists.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Rebus.Bus;
using Rebus.Config;

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
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc();
        AddGiftListProjection(services);
        AddAuthentication(services, configuration);
        return services;
    }

    /// <summary>
    /// The <c>ownerId</c> index <c>myGiftLists</c> relies on (ARCHITECTURE.md "Data model") — a correctness
    /// requirement, applied once at startup rather than left to be inferred from application
    /// code, mirroring <c>GiftListsInfrastructureServiceCollectionExtensions.EnsureIndexesAsync</c>.
    /// </summary>
    public static Task EnsureIndexesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var database = serviceProvider.GetRequiredService<IMongoDatabase>();
        return GiftListProjectionRepository.EnsureIndexesAsync(database, cancellationToken);
    }

    /// <summary>
    /// Subscribes to every GiftLists integration event this read model is built from (GL-23).
    /// Called once from <c>Program.cs</c>, after the host is built — mirrors
    /// <see cref="EnsureIndexesAsync"/>'s own placement/rationale. Public, and named after the
    /// wire events only in its doc comment rather than its signature, for the same reason
    /// <c>GatewayMessageRouting</c> exists at all: Host may not reference another service's
    /// Contracts directly (CONVENTIONS.md "Project reference graph"), only call through to Infrastructure, which may.
    /// </summary>
    public static async Task SubscribeToGiftListsEventsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var bus = serviceProvider.GetRequiredService<IBus>();
        await bus.Subscribe<GiftListCreatedV1>();
        await bus.Subscribe<GiftListRenamedV1>();
        await bus.Subscribe<GiftListDeletedV1>();
        await bus.Subscribe<GiftItemAddedV1>();
        await bus.Subscribe<GiftItemRemovedV1>();
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

        services.AddScoped<IValidator<GetGiftListRequest>, GetGiftListValidator>();
        services.AddScoped<IInteractor<GetGiftListRequest, GetGiftListResponse>, GetGiftListInteractor>();

        // One open-generic decorator pair, applied to every IInteractor<,> registered above —
        // Validation, then Logging, in that order in every service (CONVENTIONS.md "Use cases").
        services.Decorate(typeof(IInteractor<,>), typeof(Validating<,>));
        services.Decorate(typeof(IInteractor<,>), typeof(Logging<,>));

        services.AddRebusHandler<GiftListCreatedV1Handler>();
        services.AddRebusHandler<GiftListRenamedV1Handler>();
        services.AddRebusHandler<GiftListDeletedV1Handler>();
        services.AddRebusHandler<GiftItemAddedV1Handler>();
        services.AddRebusHandler<GiftItemRemovedV1Handler>();

        services.AddGraphQLServer().AddQueryType<GiftListQueries>();
    }

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
