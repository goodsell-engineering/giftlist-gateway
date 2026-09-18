using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.ViewGiftList;

/// <summary>
/// The named input port behind every surface that shows one gift list to one viewer
/// (CONVENTIONS.md "Naming"): GraphQL's owner-scoped <c>giftList(id)</c>, its unauthenticated
/// <c>sharedGiftList(token)</c>, and the share-token-scoped <c>sharedGiftListChanged(token)</c>
/// subscription. One port, because ARCHITECTURE.md "Defence in depth on the owner-facing path"
/// puts the reservation-visibility rule in exactly one interactor — "one rule, one
/// implementation, or the guarantee leaks through whichever path forgot it".
/// </summary>
/// <remarks>
/// GL-38 absorbed GL-23's <c>IGetGiftList</c> and GL-32's <c>IGetSharedGiftList</c> outright,
/// as the latter's own doc comment anticipated: the two were the owner and guest branches of this
/// rule written before there was any reservation data for the rule to be about. What they left
/// behind is kept — the anonymous response type still physically cannot carry <c>ownerId</c> or
/// the token (<see cref="SharedGiftListView"/>), and the share token is still shape-checked at
/// the boundary (<see cref="ViewGiftListValidator"/>).
/// </remarks>
public interface IViewGiftList : IInteractor<ViewGiftListRequest, ViewGiftListResponse>;
