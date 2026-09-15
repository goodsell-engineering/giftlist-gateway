using BuildingBlocks.Results;

namespace Gateway.Application.Common;

/// <summary>
/// Validates a use-case request before its interactor runs, as the request-shaped half of the
/// validation decorator (CONVENTIONS.md "Use cases" — cross-cutting concerns are decorators, not logic
/// inlined into an interactor). Hand-rolled rather than a third-party validation library:
/// Application references only Domain and BuildingBlocks (CONVENTIONS.md "Project reference graph"), full stop.
/// </summary>
public interface IValidator<in TRequest>
{
    Result Validate(TRequest request);
}
