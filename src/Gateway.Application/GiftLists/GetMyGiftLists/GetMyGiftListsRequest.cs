namespace Gateway.Application.GiftLists.GetMyGiftLists;

/// <summary><paramref name="RequesterId"/> is the caller's own id, taken from the JWT (ARCHITECTURE.md "Auth & sharing") — never a client-supplied argument, so this query can only ever return the caller's own lists.</summary>
public sealed record GetMyGiftListsRequest(Guid RequesterId);
