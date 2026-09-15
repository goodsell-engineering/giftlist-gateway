namespace Gateway.Application.GiftLists.GetMyGiftLists;

public sealed record GetMyGiftListsResponse(IReadOnlyList<GiftListProjection> GiftLists);
