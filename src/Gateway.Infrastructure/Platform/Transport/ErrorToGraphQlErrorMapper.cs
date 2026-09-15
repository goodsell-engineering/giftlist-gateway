using BuildingBlocks.Transport;
using HotChocolate;
// Aliased, not `using BuildingBlocks.Results;` — this file also needs `HotChocolate.Error`
// (indirectly, via IError/ErrorBuilder), and the two bare "Error" names collide (CS0104).
using Error = BuildingBlocks.Results.Error;

namespace Gateway.Infrastructure.Platform.Transport;

/// <summary>
/// Turns a failed use-case <see cref="Error"/> into the <see cref="GraphQLException"/> that
/// actually reaches the browser (GL-23) — the GraphQL-side counterpart of
/// <see cref="ErrorToRpcExceptionMapper"/>. Every Gateway GraphQL resolver should throw through
/// this rather than building an <see cref="IError"/> by hand, so a failed query surfaces the same
/// two things every failure in this codebase does: a stable <see cref="Error.Code"/> a client can
/// branch on, and the <see cref="ErrorKindTransportMapping.ToGraphQlCode"/> category HotChocolate
/// puts on <c>extensions.code</c> (CONVENTIONS.md "Errors").
/// </summary>
public static class ErrorToGraphQlErrorMapper
{
    /// <summary>
    /// The extension key <see cref="Error.Code"/> travels on — mirrors
    /// <see cref="ErrorToRpcExceptionMapper.ErrorCodeTrailerName"/>'s own role for grpc-web.
    /// </summary>
    public const string ErrorCodeExtensionKey = "errorCode";

    public static IError ToGraphQlError(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return ErrorBuilder.New()
            .SetMessage(error.Message)
            .SetCode(error.Kind.ToGraphQlCode())
            .SetExtension(ErrorCodeExtensionKey, error.Code)
            .Build();
    }

    public static GraphQLException ToGraphQlException(this Error error) => new(error.ToGraphQlError());
}
