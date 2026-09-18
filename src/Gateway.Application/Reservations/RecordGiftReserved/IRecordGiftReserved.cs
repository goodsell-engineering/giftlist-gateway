using Gateway.Application.Common;

namespace Gateway.Application.Reservations.RecordGiftReserved;

/// <summary>The named input port for "record that Reservations reserved an item" (CONVENTIONS.md "Naming").</summary>
public interface IRecordGiftReserved : IInteractor<RecordGiftReservedRequest, RecordGiftReservedResponse>;
