using BuildingBlocks.Results;

namespace Gateway.Application.GiftLists;

/// <summary>
/// Stable, machine-readable error codes for the Gateway's gift-list read-model use cases
/// (CONVENTIONS.md "Errors"), all <c>gateway.&lt;code&gt;</c> — two segments, applied uniformly.
/// </summary>
public static class GiftListErrors
{
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

    /// <summary>Shared across every request field that is a required id and arrived as <see cref="Guid.Empty"/> — the same semantic regardless of which field or which use case caught it.</summary>
    public static readonly Error InvalidId = new(
        "gateway.invalid_id", "A required identifier was missing or invalid.", ErrorKind.Validation);

    /// <summary>
    /// GL-71: <c>CreateGiftListRequest.ExpiresAt</c> is a proto3 message field, which carries
    /// presence but no way to be marked required — an omitted value deserializes to <c>null</c>,
    /// not a default <c>Timestamp</c>, so this is a distinct semantic from <see cref="InvalidId"/>
    /// rather than a reuse of it.
    /// </summary>
    public static readonly Error MissingExpiry = new(
        "gateway.missing_expiry", "An expiry date is required.", ErrorKind.Validation);
}
