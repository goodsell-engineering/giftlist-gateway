namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Share tokens for tests, in the one shape GiftLists can actually emit: exactly 21 base62
/// characters, the invariant of <c>GiftLists.Domain.GiftLists.ShareToken</c> (ARCHITECTURE.md
/// "Auth &amp; sharing" — "~21 chars, base62").
/// </summary>
/// <remarks>
/// <para>
/// GL-104: this suite used to fabricate <c>$"share-{Guid.NewGuid():N}"</c> — 38 characters with a
/// hyphen in it — which no publisher could ever produce and which GL-32's boundary shape check on
/// <c>sharedGiftList(token)</c> rejects outright. Test data that the production rule forbids is
/// worse than none: it makes a test pass against a token the system will never see. Exists as a
/// helper rather than nine copies of the expression so the next test to need one does not invent
/// a tenth shape.
/// </para>
/// <para>
/// Batch 34 review: this drew from <c>Guid.ToString("N")</c> at first, which is hex — it can never
/// emit an uppercase letter or any letter past <c>f</c>, so it covered roughly a third of the
/// alphabet the rule allows while claiming to cover it. Hex is a legal subset, so nothing failed;
/// it just meant no test in the suite ever presented a token whose case mattered, and the token
/// lookup's case sensitivity is a real property of <c>FindByShareTokenAsync</c> (Mongo's default
/// collation compares strings byte for byte). Drawing from the whole alphabet is three lines, and
/// the alternative — narrowing this comment to say "hex" — would have left that property untested
/// on purpose. <see cref="SharedGiftListTests"/> pins it deterministically as well, since a random
/// draw is evidence rather than a guarantee.
/// </para>
/// <para>
/// <see cref="Random.Shared"/>, not a cryptographic source: these are fixtures, never real
/// capabilities, so unguessability is not a property under test here. <c>ShareTokenGenerator</c>
/// in <c>giftlist-giftlists</c> owns that, on the production side.
/// </para>
/// </remarks>
internal static class ShareTokens
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private const int Length = 21;

    /// <summary>A fresh, valid token drawn from the whole base62 alphabet.</summary>
    public static string New() => new(Random.Shared.GetItems<char>(Base62, Length));
}
