using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using Gateway.Application.Common;

namespace Gateway.Application.GiftLists.GetSharedGiftList;

/// <summary>
/// Shape-checks the share token at the boundary, before anything reaches Mongo. This is the real
/// control on an unauthenticated surface: without it a caller who has never held a share link can
/// drive arbitrary-length, arbitrary-content strings straight at a database query (GL-32 review
/// obligation, Batch 33). The validation decorator runs ahead of the interactor
/// (CONVENTIONS.md "Use cases"), so a malformed token never reaches
/// <c>IGiftListProjectionRepository</c> at all.
/// </summary>
/// <remarks>
/// <para>
/// The rule — exactly 21 characters, base62 — is GiftLists' own: it is the invariant of
/// <c>GiftLists.Domain.GiftLists.ShareToken</c>, which sources it from ARCHITECTURE.md
/// "Auth &amp; sharing" ("an opaque unguessable <c>shareToken</c> (~21 chars, base62)"). The
/// Gateway has no Domain project and takes no dependency on GiftLists' domain
/// (CONVENTIONS.md "Project reference graph"), so the rule is restated here rather than shared.
/// That restatement is a deliberate, cited duplicate of one line of another service's invariant,
/// not an accident: if GiftLists ever changes the token shape, this constant changes with it and
/// the two are found by searching for the rule, not by a compiler error.
/// </para>
/// </remarks>
internal sealed partial class GetSharedGiftListValidator : IValidator<GetSharedGiftListRequest>
{
    public Result Validate(GetSharedGiftListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ShareTokenPattern().IsMatch(request.ShareToken)
            ? Result.Success()
            : GiftListErrors.InvalidShareToken;
    }

    /// <summary>
    /// <c>\A</c>/<c>\z</c>, deliberately, not <c>^</c>/<c>$</c>. In .NET <c>$</c> matches at the
    /// end of input <em>or immediately before a trailing newline</em>, so the obvious
    /// <c>^[0-9A-Za-z]{21}$</c> accepts a 22-character token ending in <c>\n</c> — and a GraphQL
    /// variable is a JSON string, so an anonymous caller can send exactly that (Batch 34 review).
    /// The consequence was small (an equality match on an indexed field, so the caller reached
    /// <c>not_found</c> instead of <c>invalid_share_token</c>) but it made this file's own summary
    /// false, which is the part that matters: a boundary guard that overclaims is worse than one
    /// that is merely narrow. <c>\z</c> is the true end of input.
    /// </summary>
    [GeneratedRegex(@"\A[0-9A-Za-z]{21}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ShareTokenPattern();
}
