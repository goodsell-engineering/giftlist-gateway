using System.IdentityModel.Tokens.Jwt;
using Gateway.Application.GiftLists;
using Gateway.Infrastructure.Platform.Transport;
using Microsoft.AspNetCore.Http;

namespace Gateway.Infrastructure.Platform.Security;

/// <summary>
/// Reads the caller's own id off the validated JWT (ARCHITECTURE.md "Auth & sharing") — the one thing every
/// owner-scoped GraphQL resolver needs before it can call an interactor. <see cref="RequireUserId"/>
/// is deliberately not a <see cref="Result{T}"/>-returning method like every use case: whether the
/// caller is who they claim to be is a transport-level authentication fact checked before a
/// request even reaches an interactor, not a use-case outcome — the same reason
/// <c>AuthGrpcService</c> throws an <c>RpcException</c> directly rather than routing "no token"
/// through the request/reply bridge.
/// </summary>
internal static class HttpContextExtensions
{
    /// <summary>
    /// The caller's own id, from the JWT's <c>sub</c> claim on <see cref="HttpContext.User"/>.
    /// Throws a <see cref="GraphQLException"/> mapped from <see cref="ErrorKind.Unauthenticated"/>
    /// if the token is missing, invalid, or carries no parseable <c>sub</c> —
    /// <c>app.UseAuthentication()</c> populates <see cref="HttpContext.User"/> without rejecting
    /// the request outright, so this is the one place that actually turns "no/bad token" into a
    /// GraphQL error.
    /// </summary>
    public static Guid RequireUserId(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var principal = httpContext.User;
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(subject, out var userId))
        {
            // GL-38: the same gateway.unauthenticated ViewGiftListInteractor gives an Anonymous
            // viewer — one code for one semantic (CONVENTIONS.md "Errors").
            throw GiftListErrors.Unauthenticated.ToGraphQlException();
        }

        return userId;
    }
}
