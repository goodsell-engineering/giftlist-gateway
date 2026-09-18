using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists;

/// <summary>
/// Stable, machine-readable error codes for the Gateway's gift-list read-model use cases
/// (CONVENTIONS.md "Errors"), all <c>gateway.&lt;code&gt;</c> — two segments, applied uniformly.
/// </summary>
public static class GiftListErrors
{
    /// <summary>
    /// "There is no such gift list", whichever way it was asked for — by id (<c>giftList(id)</c>)
    /// or by share token (<c>sharedGiftList(token)</c>, GL-32, including a token that resolved to
    /// a list that has since been deleted). One code, because a code names a semantic and not the
    /// place it was raised (CONVENTIONS.md "Errors"). The wording stays as it is: the message is
    /// pinned by <c>giftlist-web</c>'s error fixtures, the code is what a client branches on, and
    /// rewording it for the token path would be a cross-repo change for no behavioural gain.
    /// </summary>
    public static readonly Error NotFound = new(
        "gateway.not_found", "No gift list exists with this id.", ErrorKind.NotFound);

    /// <summary>
    /// "You may not do this" (403), not "log in again" (401) — the caller is authenticated (a
    /// valid JWT reached the interactor), just not the list's owner. Mirrors
    /// <c>GiftLists.Application.GiftLists.GiftListErrors.Forbidden</c> exactly — the same
    /// "owner queries require JWT + ownership check" rule (ARCHITECTURE.md "Auth & sharing"), enforced a second
    /// time here because the Gateway's read model is a different service's data with its own
    /// access check, not something GiftLists' own check can cover on the Gateway's behalf.
    /// </summary>
    public static readonly Error Forbidden = new(
        "gateway.forbidden", "You do not own this gift list.", ErrorKind.Forbidden);

    /// <summary>
    /// "Log in again" (401), not "you may not do this" (403): the caller presented no credential
    /// at all — no JWT subject and no share token. GL-38 moved this here from
    /// <c>Gateway.Infrastructure.Platform.Security.HttpContextExtensions</c>, which raises it at
    /// the transport when a JWT is missing or unparseable, so that <c>ViewGiftListInteractor</c>'s
    /// <c>Anonymous</c> branch and that transport check share the one code rather than each
    /// minting their own for the same semantic (CONVENTIONS.md "Errors").
    /// </summary>
    public static readonly Error Unauthenticated = new(
        "gateway.unauthenticated", "A valid access token is required.", ErrorKind.Unauthenticated);

    /// <summary>Shared across every request field that is a required id and arrived as <see cref="Guid.Empty"/> — the same semantic regardless of which field or which use case caught it.</summary>
    public static readonly Error InvalidId = new(
        "gateway.invalid_id", "A required identifier was missing or invalid.", ErrorKind.Validation);

    /// <summary>
    /// GL-32: the share token failed the shape check at the boundary
    /// (<c>GetSharedGiftListValidator</c>) — it is not 21 base62 characters, so no gift list could
    /// ever carry it and nothing was looked up. A distinct semantic from <see cref="NotFound"/>,
    /// which means "well-formed, but no list has it": telling an anonymous caller the difference
    /// costs nothing (the token's shape is visible in every share URL) and stops a client with a
    /// typo from being told to go looking for a deleted list.
    /// </summary>
    public static readonly Error InvalidShareToken = new(
        "gateway.invalid_share_token", "A share token must be 21 alphanumeric characters.", ErrorKind.Validation);

    /// <summary>
    /// GL-71: <c>CreateGiftListRequest.ExpiresAt</c> is a proto3 message field, which carries
    /// presence but no way to be marked required — an omitted value deserializes to <c>null</c>,
    /// not a default <c>Timestamp</c>, so this is a distinct semantic from <see cref="InvalidId"/>
    /// rather than a reuse of it.
    /// </summary>
    public static readonly Error MissingExpiry = new(
        "gateway.missing_expiry", "An expiry date is required.", ErrorKind.Validation);
}
