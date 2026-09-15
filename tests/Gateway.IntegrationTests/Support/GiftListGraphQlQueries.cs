namespace Gateway.IntegrationTests.Support;

/// <summary>The two GraphQL queries GL-23 adds — kept as raw document text rather than a generated client (see <c>GraphQlClient</c>'s own doc comment).</summary>
internal static class GiftListGraphQlQueries
{
    public const string MyGiftLists = """
        query {
          myGiftLists {
            listId
            ownerId
            name
            expiresAt
            shareToken
            createdAt
            items { itemId name description url }
          }
        }
        """;

    public const string GiftList = """
        query($id: UUID!) {
          giftList(id: $id) {
            listId
            ownerId
            name
            expiresAt
            shareToken
            createdAt
            items { itemId name description url }
          }
        }
        """;
}
