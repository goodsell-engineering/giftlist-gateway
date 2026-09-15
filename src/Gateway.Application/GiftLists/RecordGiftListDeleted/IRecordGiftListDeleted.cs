using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.RecordGiftListDeleted;

/// <summary>The named input port for "record that GiftLists deleted a list" (CONVENTIONS.md "Naming").</summary>
public interface IRecordGiftListDeleted : IInteractor<RecordGiftListDeletedRequest, RecordGiftListDeletedResponse>;
