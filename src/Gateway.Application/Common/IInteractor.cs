using BuildingBlocks.Results;

namespace Gateway.Application.Common;

/// <summary>
/// The shape every use case implements: one request in, one <see cref="Result{T}"/> out
/// (CONVENTIONS.md "Use cases"). Mirrors <c>GiftLists.Application.Common.IInteractor</c> (GL-16/GL-20) —
/// see that type's doc comment for why each use case still declares its own specifically-named,
/// empty port (<c>IRecordGiftListCreated</c>, <c>IViewGiftList</c>, ...) purely for the
/// naming-convention text scan, while the composition root and the
/// <see cref="Validating{TRequest,TResponse}"/>/<c>Logging&lt;,&gt;</c> decorators all key on this
/// generic interface instead — that is what lets one decorator pair cover every interactor in
/// this service, including the GraphQL query interactors as well as the Rebus-handler-driven
/// projection ones.
/// </summary>
public interface IInteractor<in TRequest, TResponse>
{
    Task<Result<TResponse>> Handle(TRequest request, CancellationToken cancellationToken);
}
