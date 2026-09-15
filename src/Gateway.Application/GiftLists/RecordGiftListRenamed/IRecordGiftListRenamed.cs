using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftListRenamed;

/// <summary>The named input port for "record that GiftLists renamed a list" (CONVENTIONS.md "Naming").</summary>
public interface IRecordGiftListRenamed : IInteractor<RecordGiftListRenamedRequest, RecordGiftListRenamedResponse>;
