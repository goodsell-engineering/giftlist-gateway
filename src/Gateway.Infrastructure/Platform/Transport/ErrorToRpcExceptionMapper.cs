using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Results;
using BuildingBlocks.Transport;
using Grpc.Core;
// GL-23 review: this project now also carries HotChocolate's implicit global `using HotChocolate;`
// (added for the GraphQL surface), and HotChocolate.Error collides with BuildingBlocks.Results.Error
// (CS0104) — aliased so this pre-existing grpc-web mapper needs no other change.
using Error = BuildingBlocks.Results.Error;

namespace Gateway.Infrastructure.Platform.Transport;

/// <summary>
/// Turns a failed use-case <see cref="Error"/> into the <see cref="RpcException"/> that actually
/// reaches the browser (GL-18) — the one hop <see cref="ErrorKindTransportMapping"/> cannot make
/// itself, since BuildingBlocks may not reference <c>Grpc.*</c> (see
/// <see cref="GrpcStatusCode"/>'s remarks, which name this mapper as the pinned cast site).
/// Every Gateway grpc-web endpoint should throw through this rather than constructing a
/// <see cref="Status"/> by hand, so "duplicate email -&gt; ABORTED", "bad credentials -&gt;
/// UNAUTHENTICATED" and "reply timeout -&gt; UNAVAILABLE" (Phase 1 exit criteria) come from one
/// place, not one per handler.
///
/// Named <c>...Platform.Transport</c>, not <c>...Platform.Grpc</c> (GL-18 review): a namespace
/// ending in <c>Grpc</c> shadows the root <c>Grpc</c> namespace from inside this file, so a
/// future fully-qualified <c>Grpc.Core.Status</c> would bind confusingly (or need
/// <c>global::Grpc.Core</c>) rather than resolving where it looks like it should.
///
/// Public, unlike <see cref="Gateway.Infrastructure.Users.Grpc.AuthGrpcService"/> — two other
/// things outside this assembly need <see cref="ErrorCodeTrailerName"/> by name rather than a
/// re-typed copy of the same string literal: Gateway.Host's CORS policy (<c>WithExposedHeaders</c>
/// must list it or the browser cannot read it) and Gateway.IntegrationTests (asserting the
/// trailer actually carries the code it claims to).
/// </summary>
public static class ErrorToRpcExceptionMapper
{
    /// <summary>
    /// The gRPC trailer <see cref="Error.Code"/> travels on (CONVENTIONS.md "Errors": <c>Code</c> is
    /// the stable, machine-readable half). <see cref="Status"/> carries only a code + a free-text
    /// message — with no trailer, <c>messaging.reply_timeout</c> (still might be working) and a
    /// genuine broker outage both arrive as bare UNAVAILABLE, which is exactly the distinction
    /// <see cref="RequestReplyErrors.ReplyTimeoutCode"/>'s own doc comment says a caller needs
    /// (ARCHITECTURE.md "Command → event flow"'s "still working on it" fallback) — the Gateway is that caller's
    /// caller-facing hop. Lowercase per gRPC's metadata key rules. Gateway.Host's CORS policy
    /// must expose this header (grpc-web trailers-only responses are promoted to ordinary HTTP
    /// headers) or the browser cannot read it despite the server sending it.
    /// </summary>
    public const string ErrorCodeTrailerName = "giftlist-error-code";

    public static RpcException ToRpcException(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // Numeric cast, not a second switch: GrpcStatusCode's values are defined to match
        // Grpc.Core.StatusCode exactly (both restate the same gRPC spec) — pinned by
        // GrpcStatusCodeCastTests so a future divergence between the two enums fails loudly here
        // rather than silently mis-mapping every error.
        var statusCode = (StatusCode)(int)error.Kind.ToGrpcStatus();

        var trailers = new Metadata { { ErrorCodeTrailerName, error.Code } };

        // error.Message never contains PII (CONVENTIONS.md "Errors") and is written to be shown to a
        // user for every Error this codebase raises deliberately (e.g. UserErrors) — except the
        // request/reply bridge's own timeout message, which is BuildingBlocks-wide diagnostic
        // text (it names the internal .NET request type and the server's configured timeout
        // budget) never written with "safe to hand to an untrusted browser" in mind. The code on
        // the trailer above already carries everything a client needs to branch on; the message
        // here is redacted to something equally generic instead of also being redacted at the
        // source, which would weaken the diagnostic for every other (trusted, server-to-server)
        // caller of the bridge.
        var message = error.Code == RequestReplyErrors.ReplyTimeoutCode
            ? "The request could not be completed in time. It may still be processing — please try again."
            : error.Message;

        return new RpcException(new Status(statusCode, message), trailers);
    }
}
