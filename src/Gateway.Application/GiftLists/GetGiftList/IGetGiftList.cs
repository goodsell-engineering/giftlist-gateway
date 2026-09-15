using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.GetGiftList;

/// <summary>The named input port behind GraphQL's <c>giftList(id)</c> query (CONVENTIONS.md "Naming").</summary>
public interface IGetGiftList : IInteractor<GetGiftListRequest, GetGiftListResponse>;
