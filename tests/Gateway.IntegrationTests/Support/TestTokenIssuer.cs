using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Mints an access token exactly the way <c>Identity.Infrastructure.Platform.Security.JwtTokenIssuer</c>
/// does (same claim, same algorithm), signed with <see cref="TestJwtKeys.Key"/>'s private half —
/// this suite stands in for Identity for the one thing GL-23's tests need from it, a token the
/// Gateway will accept, without running Identity itself (CONVENTIONS.md "Testing": "other services are
/// not run").
/// </summary>
internal static class TestTokenIssuer
{
    public static string IssueAccessToken(Guid userId) => IssueAccessTokenWithSubject(userId.ToString());

    /// <summary>
    /// GL-71 Batch 14 review (S3): a validly-signed, non-expired token whose <c>sub</c> claim is
    /// not a <see cref="Guid"/> — the one branch of
    /// <c>ServerCallContextExtensions.RequireUserId</c>'s guard that can actually fire once
    /// <c>RequireAuthorization()</c> is in place (an unauthenticated caller never reaches the
    /// handler at all, so <c>Identity.IsAuthenticated != true</c> is otherwise dead code in
    /// production).
    /// </summary>
    public static string IssueAccessTokenWithSubject(string subject)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(TestJwtKeys.Key), SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, subject) };

        var token = new JwtSecurityToken(
            issuer: TestJwtKeys.Issuer,
            audience: TestJwtKeys.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
