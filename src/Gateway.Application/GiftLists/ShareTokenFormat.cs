using System.Text.RegularExpressions;

namespace Gateway.Application.GiftLists;

/// <summary>
/// The share-token shape check, in one place (GL-37) — <c>ViewGiftListValidator</c> and, since
/// GL-37, the reservation grpc-web surface (<c>Gateway.Infrastructure.Reservations.Grpc.ReservationsGrpcService</c>)
/// both need to reject a malformed token before it ever reaches Mongo or the request/reply
/// bridge, so this is the single restatement of GiftLists' own <c>ShareToken</c> invariant
/// (CONVENTIONS.md "Project reference graph" — the Gateway has no Domain project and takes no
/// dependency on GiftLists' domain) rather than a second copy of it inside this same service.
/// Cross-service duplication of the rule (this restatement itself) stays deliberate; this type
/// only removes the intra-service copy.
/// </summary>
public static partial class ShareTokenFormat
{
    public static bool IsValid(string shareToken) => ShareTokenPattern().IsMatch(shareToken);

    /// <summary>
    /// <c>\A</c>/<c>\z</c>, deliberately, not <c>^</c>/<c>$</c>. In .NET <c>$</c> matches at the
    /// end of input <em>or immediately before a trailing newline</em>, so the obvious
    /// <c>^[0-9A-Za-z]{21}$</c> accepts a 22-character token ending in <c>\n</c> — and a GraphQL
    /// variable is a JSON string, so an anonymous caller can send exactly that (Batch 34 review).
    /// <c>\z</c> is the true end of input.
    /// </summary>
    [GeneratedRegex(@"\A[0-9A-Za-z]{21}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ShareTokenPattern();
}
