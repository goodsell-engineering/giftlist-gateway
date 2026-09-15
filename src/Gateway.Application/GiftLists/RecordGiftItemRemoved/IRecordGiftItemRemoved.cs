using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftItemRemoved;

/// <summary>The named input port for "record that GiftLists removed an item" (CONVENTIONS.md "Naming").</summary>
public interface IRecordGiftItemRemoved : IInteractor<RecordGiftItemRemovedRequest, RecordGiftItemRemovedResponse>;
