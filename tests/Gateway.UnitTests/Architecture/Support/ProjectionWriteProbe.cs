using System.Text.RegularExpressions;

namespace ArchitectureTests.Support;

/// <summary>
/// The detector behind CONVENTIONS.md "Messaging"'s "projections are written as upserts, never blind
/// inserts" [AT] rule: given the text of a source file, decide whether it is projection
/// persistence code, where it writes, and whether any of those writes is an unguarded insert.
///
/// It lives here rather than inside <c>ProjectionWriteRuleTests</c> because two different tests
/// need it, and deliberately so. The rule itself scans whatever projection code its repo happens
/// to contain, and a self-test over hand-written samples only ever constrains the detector as
/// applied to those samples — so a repo whose real code the detector has stopped recognising
/// stays green on both. Binding it to a real file takes a third test that names one
/// (<c>Gateway.UnitTests/Architecture/ProjectionWriteBindingTests</c>), and that test needs this
/// logic to be reachable from outside the class that consumes it.
/// </summary>
internal static class ProjectionWriteProbe
{
    /// <summary>
    /// An insert that is safe to repeat for a reason this scan cannot see (CONVENTIONS.md "Messaging"'s third mechanism,
    /// a unique index on the business key, is the likely one) can carry this comment on its own
    /// line or the three lines above to opt out — with a reason. Same escape-hatch pattern as
    /// <c>NamingConventionTests</c>' past-tense opt-out.
    /// </summary>
    public const string OptOutMarker = "architecture:allow-unguarded-insert";

    /// <summary>
    /// How far after an insert a duplicate-key <c>catch</c> may appear and still count as
    /// guarding it. A catch that guards an insert necessarily follows it and is close by; one
    /// further down the file is guarding something else, and treating it as cover would let a
    /// single guarded insert excuse every blind one in the same class.
    /// </summary>
    private const int GuardLookaheadCharacters = 400;

    private static readonly Regex CollectionElementType =
        new(@"(?:IMongoCollection|GetCollection)\s*<\s*(?<element>[\w\.]+)\s*>", RegexOptions.Compiled);

    private static readonly Regex TypeDeclaration =
        new(@"\b(?:class|record|struct)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);

    /// <summary>
    /// Every Mongo write this scan recognises. Deliberately spelled out method by method rather
    /// than as a loose <c>Insert\w*</c>/<c>Replace\w*</c> pattern: <c>string.Replace</c> is
    /// everywhere in this codebase, and a write-site count inflated by it would make the
    /// stale-scan guard meaningless.
    /// </summary>
    private static readonly Regex WriteCall = new(
        @"\.\s*(?<method>InsertOneAsync|InsertOne|InsertManyAsync|InsertMany|ReplaceOneAsync|ReplaceOne|" +
        @"UpdateOneAsync|UpdateOne|UpdateManyAsync|UpdateMany|FindOneAndUpdateAsync|FindOneAndUpdate|" +
        @"FindOneAndReplaceAsync|FindOneAndReplace|BulkWriteAsync|BulkWrite|DeleteOneAsync|DeleteOne|" +
        @"DeleteManyAsync|DeleteMany)\s*\(" +
        @"|new\s+(?<method>InsertOneModel)\s*<",
        RegexOptions.Compiled);

    /// <summary>
    /// The rest of the Mongo surface — reads and index management. Nothing here is a write, so
    /// none of it can be a blind insert; it exists only so <see cref="LooksLikeAStaleScan"/> can
    /// tell "this file does no writes" (a read-only projection reader, which is clean, ordinary
    /// code) apart from "this rule can no longer see what this file does" (which is a broken
    /// rule). Getting that distinction wrong sends someone with correct code off to fix the rule.
    /// </summary>
    private static readonly Regex MongoOperation = new(
        @"\.\s*(?:Find|FindAsync|FindSync|Aggregate|AggregateAsync|CountDocuments|CountDocumentsAsync|" +
        @"EstimatedDocumentCount|EstimatedDocumentCountAsync|Distinct|DistinctAsync|Watch|WatchAsync)\s*\(" +
        @"|\.\s*Indexes\s*\.",
        RegexOptions.Compiled);

    private static readonly Regex DuplicateKeyGuard =
        new(@"\bcatch\b[\s\S]*?DuplicateKey", RegexOptions.Compiled);

    public sealed record WriteSite(int Line, string Method, bool IsInsert, bool IsGuarded)
    {
        public bool IsBlindInsert => IsInsert && !IsGuarded;
    }

    /// <summary>
    /// A file counts as projection persistence code when it holds a Mongo collection and the
    /// word "Projection" — CONVENTIONS.md "Naming"'s own term for a read model — appears either on the
    /// collection's element type or on a type the file declares. Anchoring on the element type
    /// as well as the declaring type is what stops the rule being dodged by writing to a
    /// projection collection from a class not called <c>*Projection*</c>; anchoring on the
    /// declared type is what still catches a projection repository whose document type is named
    /// something else. Both disjuncts have their own sample in the probe self-test, one each, so
    /// that deleting either one goes red.
    /// </summary>
    public static bool IsProjectionPersistenceFile(string text)
    {
        if (!text.Contains("IMongoCollection", StringComparison.Ordinal) &&
            !text.Contains("GetCollection<", StringComparison.Ordinal))
        {
            return false;
        }

        return NamesAProjectionCollectionElement(text) || DeclaresAProjectionType(text);
    }

    public static bool NamesAProjectionCollectionElement(string text) =>
        CollectionElementType.Matches(text)
            .Any(m => m.Groups["element"].Value.Contains("Projection", StringComparison.Ordinal));

    public static bool DeclaresAProjectionType(string text) =>
        TypeDeclaration.Matches(text)
            .Any(m => m.Groups["name"].Value.Contains("Projection", StringComparison.Ordinal));

    public static List<WriteSite> FindWriteSites(string text)
    {
        var sites = new List<WriteSite>();

        foreach (Match match in WriteCall.Matches(text))
        {
            var method = match.Groups["method"].Value;
            var isInsert = method.StartsWith("Insert", StringComparison.Ordinal);
            sites.Add(new WriteSite(
                LineOf(text, match.Index),
                method,
                isInsert,
                isInsert && IsGuarded(text, match.Index)));
        }

        return sites;
    }

    /// <summary>
    /// Whether this file has fallen out of the probe's view entirely: it holds a projection
    /// collection, but neither <see cref="WriteCall"/> nor <see cref="MongoOperation"/> matches
    /// anything in it. A file that genuinely only reads trips <see cref="MongoOperation"/> and so
    /// is not stale.
    ///
    /// <para>This guard cannot, on its own, catch <see cref="WriteCall"/> going stale against a
    /// file that also reads — which every real projection repository does. That is what
    /// <c>ProjectionWriteBindingTests</c> is for; do not read this as covering it.</para>
    /// </summary>
    public static bool LooksLikeAStaleScan(string text, IReadOnlyCollection<WriteSite> sites) =>
        sites.Count == 0 && !MongoOperation.IsMatch(text);

    private static bool IsGuarded(string text, int index) =>
        HasOptOut(text, index) || DuplicateKeyGuard.IsMatch(Window(text, index));

    private static string Window(string text, int index)
    {
        var end = Math.Min(text.Length, index + GuardLookaheadCharacters);
        return text[index..end];
    }

    /// <summary>
    /// Looks for <see cref="OptOutMarker"/> on the insert's own line or the three lines above
    /// it — three, not one, because the marker is required to carry a reason and a reason worth
    /// reading usually wraps.
    /// </summary>
    private static bool HasOptOut(string text, int index)
    {
        const int LinesOfLookback = 3;

        var start = index;
        for (var i = 0; i <= LinesOfLookback && start > 0; i++)
        {
            var previous = text.LastIndexOf('\n', Math.Max(start - 1, 0));
            if (previous < 0)
            {
                start = 0;
                break;
            }

            start = previous;
        }

        var lineEnd = text.IndexOf('\n', index);
        var end = lineEnd < 0 ? text.Length : lineEnd;
        return text[Math.Max(start, 0)..end].Contains(OptOutMarker, StringComparison.Ordinal);
    }

    private static int LineOf(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;
}
