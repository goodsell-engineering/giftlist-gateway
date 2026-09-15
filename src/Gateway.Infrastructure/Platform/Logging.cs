using Gateway.Application.Common;
using Microsoft.Extensions.Logging;

// Not `using BuildingBlocks.Results;` — this project also carries HotChocolate's implicit global
// `using GreenDonut;`, and GreenDonut.Result<T> collides with BuildingBlocks.Results.Result<T>
// (CS0104). An open generic type cannot be aliased, so Result<TResponse> below is fully
// qualified instead.

namespace Gateway.Infrastructure.Platform;

/// <summary>
/// The logging decorator (CONVENTIONS.md "Use cases"), generic over every use case — mirrors
/// <c>GiftLists.Infrastructure.Platform.Logging&lt;,&gt;</c> exactly. Lives here rather than
/// beside <c>Validating&lt;,&gt;</c> in Application/Common because it needs <see cref="ILogger"/>,
/// and Application references nothing beyond BuildingBlocks (CONVENTIONS.md "Project reference graph"). Registered after
/// <c>Validating&lt;,&gt;</c> in the composition root, so it is the outermost decorator and also
/// observes (and logs) a validation failure, not just a business one — Validation, then Logging,
/// the same order in every service.
/// </summary>
internal sealed class Logging<TRequest, TResponse>(
    IInteractor<TRequest, TResponse> inner,
    ILogger<Logging<TRequest, TResponse>> logger) : IInteractor<TRequest, TResponse>
{
    public async Task<BuildingBlocks.Results.Result<TResponse>> Handle(TRequest request, CancellationToken cancellationToken)
    {
        var result = await inner.Handle(request, cancellationToken);

        if (result.IsFailure)
        {
            // Error.Message is guaranteed PII-free (CONVENTIONS.md "Errors"), so it is safe to log
            // alongside the stable Code — neither ever identifies who made the request.
            logger.LogWarning(
                "{RequestType} failed: {ErrorCode} ({ErrorKind})",
                typeof(TRequest).Name,
                result.Error.Code,
                result.Error.Kind);
        }
        else
        {
            logger.LogInformation("{RequestType} succeeded.", typeof(TRequest).Name);
        }

        return result;
    }
}
