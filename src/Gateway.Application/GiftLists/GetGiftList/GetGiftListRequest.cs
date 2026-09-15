namespace Gateway.Application.GiftLists.GetGiftList;

/// <summary><paramref name="RequesterId"/> is the caller's own id, taken from the JWT (ARCHITECTURE.md "Auth & sharing"), checked against the projected list's <c>OwnerId</c> before it is returned.</summary>
public sealed record GetGiftListRequest(Guid ListId, Guid RequesterId);
