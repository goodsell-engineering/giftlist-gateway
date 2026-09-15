namespace Gateway.Infrastructure.Platform.Security;

/// <summary>
/// Bound from the <c>Jwt</c> configuration section — the Gateway-side half of
/// <c>Identity.Infrastructure.Platform.Security.JwtOptions</c> (ARCHITECTURE.md "Why workers still need a little HTTP": Identity
/// signs with the private key, the Gateway validates with the corresponding public key mounted
/// into it as config). The Gateway never sees <c>Jwt:SigningKeyPem</c>.
/// </summary>
public sealed class GatewayJwtOptions
{
    public const string ConfigurationSection = "Jwt";

    /// <summary>PKCS8 PEM-encoded RSA public key — the public half of Identity's signing key.</summary>
    public required string PublicKeyPem { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }
}
