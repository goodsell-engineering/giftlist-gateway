namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Share tokens for tests, in the one shape GiftLists can actually emit: exactly 21 base62
/// characters, the invariant of <c>GiftLists.Domain.GiftLists.ShareToken</c> (ARCHITECTURE.md
/// "Auth &amp; sharing" — "~21 chars, base62").
/// </summary>
/// <remarks>
/// GL-104: this suite used to fabricate <c>$"share-{Guid.NewGuid():N}"</c> — 38 characters with a
/// hyphen in it — which no publisher could ever produce and which GL-32's boundary shape check on
/// <c>sharedGiftList(token)</c> rejects outright. Test data that the production rule forbids is
/// worse than none: it makes a test pass against a token the system will never see. Exists as a
/// helper rather than nine copies of the expression so the next test to need one does not invent
/// a tenth shape.
/// </remarks>
internal static class ShareTokens
{
    /// <summary>A fresh, valid token. Hex is a subset of base62, so a trimmed GUID satisfies the rule.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..21];
}
