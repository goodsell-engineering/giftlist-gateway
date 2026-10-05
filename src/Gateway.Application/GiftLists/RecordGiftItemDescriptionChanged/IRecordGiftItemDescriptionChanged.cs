using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftItemDescriptionChanged;

/// <summary>The named input port for "record that GiftLists changed an item's description" (CONVENTIONS.md "Naming").</summary>
public interface IRecordGiftItemDescriptionChanged
    : IInteractor<RecordGiftItemDescriptionChangedRequest, RecordGiftItemDescriptionChangedResponse>;
