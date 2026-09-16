namespace Gateway.IntegrationTests.Support;

/// <summary>The GraphQL queries this suite sends — GL-23's two owner-scoped ones and GL-32's unauthenticated <c>sharedGiftList(token)</c>. Kept as raw document text rather than a generated client (see <c>GraphQlClient</c>'s own doc comment).</summary>
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

    /// <summary>
    /// GL-32's unauthenticated share-link query. Every field the anonymous type has — there is no
    /// <c>ownerId</c> and no <c>shareToken</c> to ask for (see
    /// <see cref="SharedGiftListOwnerFields"/>, which proves that by asking).
    /// </summary>
    public const string SharedGiftList = """
        query($token: String!) {
          sharedGiftList(token: $token) {
            listId
            name
            expiresAt
            items { itemId name description url }
          }
        }
        """;

    /// <summary>
    /// Deliberately invalid against the schema: asks the anonymous view for the two fields it must
    /// not have. A test that only checked the fields present would pass just as well against a
    /// response that carried these as well, so this is the query that can actually fail if
    /// <c>sharedGiftList</c> ever starts returning <c>GiftListProjection</c>.
    /// </summary>
    public const string SharedGiftListOwnerFields = """
        query($token: String!) {
          sharedGiftList(token: $token) {
            listId
            ownerId
            shareToken
          }
        }
        """;
}
