using BuildingBlocks.Results;

namespace Gateway.Application.Common;

/// <summary>
/// The validation decorator (CONVENTIONS.md "Use cases"), generic over every use case rather than
/// hand-written per port — see <see cref="IInteractor{TRequest,TResponse}"/>'s doc comment for
/// why this can be registered once, as an open generic, instead of once per interactor. The
/// wrapped interactor never re-validates its own request.
/// </summary>
internal sealed class Validating<TRequest, TResponse>(
    IValidator<TRequest> validator,
    IInteractor<TRequest, TResponse> inner) : IInteractor<TRequest, TResponse>
{
    public Task<Result<TResponse>> Handle(TRequest request, CancellationToken cancellationToken)
    {
        var validation = validator.Validate(request);
        return validation.IsFailure
            ? Task.FromResult(Result<TResponse>.Failure(validation.Error))
            : inner.Handle(request, cancellationToken);
    }
}
