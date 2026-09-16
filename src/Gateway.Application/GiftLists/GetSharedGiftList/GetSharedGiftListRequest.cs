namespace Gateway.Application.GiftLists.GetSharedGiftList;

/// <summary>
/// <paramref name="ShareToken"/> is the entire credential for this use case — a bearer capability
/// supplied by an unauthenticated caller (ARCHITECTURE.md "Auth &amp; sharing"), never read off a
/// JWT. It is shape-checked by <see cref="GetSharedGiftListValidator"/> before any query runs.
/// </summary>
public sealed record GetSharedGiftListRequest(string ShareToken);
