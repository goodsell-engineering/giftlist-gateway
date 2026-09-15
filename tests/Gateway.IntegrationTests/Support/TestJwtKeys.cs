using System.Security.Cryptography;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// A fresh RSA key pair per test process — mirrors
/// <c>Identity.IntegrationTests.Support.TestJwtKeys</c> exactly, except this suite is on the
/// <em>validating</em> side of the pair (the Gateway holds the public half, ARCHITECTURE.md
/// "Why workers still need a little HTTP"), so <see cref="IssueAccessToken"/> below signs with the private half the same in-memory
/// <see cref="RSA"/> instance already carries — no PEM round-trip needed for that side.
/// </summary>
internal static class TestJwtKeys
{
    public static readonly RSA Key = RSA.Create(2048);

    /// <summary>SubjectPublicKeyInfo PEM — the "PUBLIC KEY" form <c>RSA.ImportFromPem</c> (and Gateway's own startup wiring) accepts.</summary>
    public static string PublicKeyPem => Key.ExportSubjectPublicKeyInfoPem();

    public const string Issuer = "gateway-integration-tests";

    public const string Audience = "giftlist";
}
