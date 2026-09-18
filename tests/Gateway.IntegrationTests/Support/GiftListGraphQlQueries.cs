namespace Gateway.IntegrationTests.Support;

/// <summary>The GraphQL documents this suite sends — GL-23's two owner-scoped queries, GL-32's unauthenticated <c>sharedGiftList(token)</c>, GL-38's <c>sharedGiftListChanged(token)</c> subscription, and the introspection and deliberately-invalid documents that prove what the schema does <em>not</em> have. Kept as raw document text rather than a generated client (see <c>GraphQlClient</c>'s own doc comment).</summary>
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
    /// <see cref="SharedGiftListOwnerFields"/>, which proves that by asking), and since GL-38 the
    /// items carry <c>reserved</c> and nothing else about a reservation (see
    /// <see cref="SharedGiftListReservationDetailFields"/>, likewise).
    /// </summary>
    public const string SharedGiftList = """
        query($token: String!) {
          sharedGiftList(token: $token) {
            listId
            name
            expiresAt
            items { itemId name description url reserved }
          }
        }
        """;

    /// <summary>
    /// GL-38: deliberately invalid against the schema — asks the guest's item for every field a
    /// reservation could be correlated on. The only reservation field the type has is
    /// <c>reserved</c> (ARCHITECTURE.md "Nobody can see *who* reserved"); each of these must be
    /// rejected by validation before any resolver runs.
    /// </summary>
    public const string SharedGiftListReservationDetailFields = """
        query($token: String!) {
          sharedGiftList(token: $token) {
            items { itemId reservedAt reservedBy reservationId releaseSecret }
          }
        }
        """;

    /// <summary>
    /// GL-38: deliberately invalid against the schema — asks the owner's item for
    /// <c>reserved</c>. The owner-facing type has no such field, by construction
    /// (ARCHITECTURE.md "Defence in depth on the owner-facing path").
    /// </summary>
    public const string GiftListReservedField = """
        query($id: UUID!) {
          giftList(id: $id) {
            items { itemId reserved }
          }
        }
        """;

    /// <summary>GL-38: the one subscription, share-token scoped. Selects the same fields as <see cref="SharedGiftList"/>.</summary>
    public const string SharedGiftListChanged = """
        subscription($token: String!) {
          sharedGiftListChanged(token: $token) {
            listId
            name
            expiresAt
            items { itemId name description url reserved }
          }
        }
        """;

    /// <summary>GL-38: the subscription counterpart of <see cref="SharedGiftListReservationDetailFields"/> — the same fields must be just as absent on the pushed payload.</summary>
    public const string SharedGiftListChangedReservationDetailFields = """
        subscription($token: String!) {
          sharedGiftListChanged(token: $token) {
            items { itemId reservedAt reservedBy reservationId releaseSecret }
          }
        }
        """;

    /// <summary>
    /// GL-38: what the schema says about itself — the whole field set of each item type and the
    /// whole set of subscription fields. Positive field-by-field queries prove a field exists;
    /// only introspection can prove a type has <em>exactly</em> these fields and no others.
    /// </summary>
    public const string ReservationSurfaceIntrospection = """
        query {
          guestItem: __type(name: "SharedGiftItemView") { fields { name type { kind name ofType { name } } } }
          ownerItem: __type(name: "GiftItemProjection") { fields { name } }
          __schema { subscriptionType { fields { name args { name } } } }
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
