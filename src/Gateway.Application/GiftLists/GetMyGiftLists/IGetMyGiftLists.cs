using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.GetMyGiftLists;

/// <summary>The named input port behind GraphQL's <c>myGiftLists</c> query (CONVENTIONS.md "Naming").</summary>
public interface IGetMyGiftLists : IInteractor<GetMyGiftListsRequest, GetMyGiftListsResponse>;
