using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftListCreated;

/// <summary>
/// The named input port for "record that GiftLists created a list" (CONVENTIONS.md "Naming"). Declared
/// for the naming convention and for readability on
/// <see cref="RecordGiftListCreatedInteractor"/>'s own base list — see
/// <see cref="IInteractor{TRequest,TResponse}"/>'s doc comment for why nothing resolves this
/// specific type from the container.
/// </summary>
public interface IRecordGiftListCreated : IInteractor<RecordGiftListCreatedRequest, RecordGiftListCreatedResponse>;
