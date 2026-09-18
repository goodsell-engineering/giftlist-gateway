namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// The whole request is the viewer: which list is being asked for is a property of who is asking
/// (<see cref="ViewerContext"/>), and the same is true of the credential. Shape-checked by
/// <see cref="ViewGiftListValidator"/> before any query runs.
/// </summary>
public sealed record ViewGiftListRequest(ViewerContext Viewer);
