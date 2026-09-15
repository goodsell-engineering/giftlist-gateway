namespace Gateway.Application.GiftLists.RecordGiftListCreated;

/// <summary>
/// No data to carry back — the caller is a Rebus handler with nobody waiting synchronously on a
/// reply (mirrors GiftLists' own fire-and-forget command handlers). Still returned through
/// <c>Result&lt;T&gt;</c>, not <c>void</c>, so this use case goes through the same
/// Validating/Logging decorator pipeline as every other interactor (CONVENTIONS.md "Use cases").
/// </summary>
public sealed record RecordGiftListCreatedResponse;
