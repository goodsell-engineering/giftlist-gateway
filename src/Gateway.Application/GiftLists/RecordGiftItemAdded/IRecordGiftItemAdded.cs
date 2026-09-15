using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftItemAdded;

/// <summary>The named input port for "record that GiftLists added an item" (CONVENTIONS.md "Naming").</summary>
public interface IRecordGiftItemAdded : IInteractor<RecordGiftItemAddedRequest, RecordGiftItemAddedResponse>;
