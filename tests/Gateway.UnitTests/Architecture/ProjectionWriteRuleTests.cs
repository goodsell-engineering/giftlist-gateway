using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// CONVENTIONS.md "Messaging" [AT]: "Projections are written as upserts, never blind inserts."
///
/// GL-61 removed the shared <c>processedMessages</c> inbox from CONVENTIONS.md "Messaging" (it was mark-first, which
/// silently drops any message whose handler then throws — the full reasoning is in CONVENTIONS.md "Messaging"'s "Why
/// there is no inbox"). With the inbox gone, this rule is no longer a second line of defence
/// behind a dedupe step: it is one of the two things that actually keep a read model correct
/// under at-least-once delivery. A rule that load-bearing should not be enforced by review,
/// which in this project has repeatedly meant not enforced at all.
///
/// <para><b>This test enforces only the "never blind inserts" half</b>, which is why it is named
/// for that half and not for the whole sentence. "Written as upserts" has legitimate variants —
/// a plain upsert, a version-filtered compare-and-set replace with a retry loop (what
/// <c>Gateway.Infrastructure.GiftLists.Persistence.GiftListProjectionRepository</c> actually
/// does), a find-modify-conditionally-write cycle — and choosing between them is a design
/// judgement no regex should make. That half is carried as a named, visible
/// <c>Skip</c> in <see cref="DeferredRuleTests"/> rather than left to a reader to infer from
/// this comment.</para>
///
/// <para><b>Why a text scan and not reflection.</b> Same reason as
/// <see cref="NamingConventionTests"/>: a Mongo write is a call, not a type, so there is no
/// marker to reflect over — and a scan needs nothing built to run.</para>
///
/// <para><b>What makes this rule's green mean something.</b> Three tests, and it takes all
/// three — this one alone is vacuous in four of the five repos it is copied into, since they
/// contain no projection code at all:
/// <list type="bullet">
/// <item>this scan, over whatever projection code the repo really has;</item>
/// <item><see cref="TheProbe_ShouldJudgeEveryKnownProjectionWriteShapeCorrectly"/>, which pins
/// the detector's verdicts against samples that name their own expected answer — but only ever
/// constrains the detector as applied to those samples;</item>
/// <item><c>Gateway.UnitTests/Architecture/ProjectionWriteBindingTests</c>, which binds the
/// detector to a named real file, and is the only one of the three that goes red if the
/// detector stops recognising production code. It is Gateway-only because Gateway is the only
/// repo with a projection to bind to.</item>
/// </list></para>
/// </summary>
public class ProjectionWriteRuleTests
{
    [Fact]
    public void Projections_ShouldNeverBeWrittenAsBlindInserts()
    {
        // Arrange
        var offenders = new List<string>();
        var scannedFiles = 0;
        var scannedWriteSites = 0;

        // Act
        foreach (var (_, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!ProjectionWriteProbe.IsProjectionPersistenceFile(text))
            {
                continue;
            }

            scannedFiles++;
            var sites = ProjectionWriteProbe.FindWriteSites(text);
            scannedWriteSites += sites.Count;

            // Non-vacuity, per file. The trigger is "this file holds a projection collection and
            // yet does nothing this rule recognises" — not "does no writes". A projection reader
            // that only queries is clean code and must pass silently; an earlier version of this
            // guard fired on exactly that and told its author to go fix the rule (GL-61 review).
            // This does not catch the write pattern going stale against a file that also reads —
            // ProjectionWriteBindingTests in Gateway.UnitTests is what covers that.
            if (ProjectionWriteProbe.LooksLikeAStaleScan(text, sites))
            {
                offenders.Add(
                    $"{relativePath}: holds a projection IMongoCollection but this rule recognised " +
                    "no Mongo operation in it at all, read or write — the rule's own patterns have " +
                    "probably gone stale, not the code");
                continue;
            }

            offenders.AddRange(sites
                .Where(s => s.IsBlindInsert)
                .Select(s => $"{relativePath}:{s.Line}: blind {s.Method}"));
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "CONVENTIONS.md \"Messaging\" [AT]: projections are never written as blind inserts. Delivery is " +
            "at-least-once, so a projection insert that is not an upsert, not guarded by a " +
            $"duplicate-key catch, and not carrying a '// {ProjectionWriteProbe.OptOutMarker}: " +
            "<reason>' comment will duplicate a row or throw the second time its message " +
            $"arrives. Scanned {scannedWriteSites} write site(s) across {scannedFiles} projection " +
            $"file(s). Offenders: {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// Pins every verdict the detector gives, against samples that name their own expected
    /// answer. Each sample fixes all five: whether each half of the two-way recognition test
    /// fires, whether any write site is seen, whether a blind insert is seen, and whether the
    /// stale-scan guard fires.
    ///
    /// <para>Two of these exist purely to hold the recognition test's disjuncts apart. Every
    /// other sample satisfies both halves at once, so before they were added, deleting either
    /// half left this test green — including the half that exists to stop the specific dodge of
    /// writing to a projection collection from a class not named <c>*Projection*</c>, which had
    /// no test at all (GL-61 review). The read-only and update-only samples are likewise
    /// regression locks, for a false positive this rule really had: clean code reported as a
    /// broken rule.</para>
    /// </summary>
    [Fact]
    public void TheProbe_ShouldJudgeEveryKnownProjectionWriteShapeCorrectly()
    {
        // Arrange
        var samples = new[]
        {
            new ProbeSample("blind insert", BlindInsertSample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: true, Blind: true, Stale: false),
            new ProbeSample("duplicate-key-guarded insert", GuardedInsertSample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: true, Blind: false, Stale: false),
            new ProbeSample("annotated insert", AnnotatedInsertSample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: true, Blind: false, Stale: false),
            new ProbeSample("upsert, no insert at all", UpsertSample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: true, Blind: false, Stale: false),
            new ProbeSample("update-only, $set/$push, no insert", UpdateOnlySample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: true, Blind: false, Stale: false),
            new ProbeSample("read-only projection reader", ReadOnlySample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: false, Blind: false, Stale: false),
            new ProbeSample("no recognised Mongo operation at all", UnrecognisedSurfaceSample,
                NamesElement: true, DeclaresType: true, AnyWriteSite: false, Blind: false, Stale: true),

            // The dodge: a class not named *Projection* writing blindly to a projection
            // collection. Recognised on the collection's element type alone.
            new ProbeSample("non-Projection class, projection collection", ElementOnlySample,
                NamesElement: true, DeclaresType: false, AnyWriteSite: true, Blind: true, Stale: false),

            // The mirror: a *Projection*-named repository whose document type is named something
            // else. Recognised on the declared type alone.
            new ProbeSample("Projection class, non-Projection collection", DeclarationOnlySample,
                NamesElement: false, DeclaresType: true, AnyWriteSite: true, Blind: false, Stale: false),
        };
        var wrongVerdicts = new List<string>();

        // Act
        foreach (var sample in samples)
        {
            var sites = ProjectionWriteProbe.FindWriteSites(sample.Source);
            var actual = new ProbeSample(
                sample.Name,
                sample.Source,
                NamesElement: ProjectionWriteProbe.NamesAProjectionCollectionElement(sample.Source),
                DeclaresType: ProjectionWriteProbe.DeclaresAProjectionType(sample.Source),
                AnyWriteSite: sites.Count > 0,
                Blind: sites.Any(s => s.IsBlindInsert),
                Stale: ProjectionWriteProbe.LooksLikeAStaleScan(sample.Source, sites));

            if (!ProjectionWriteProbe.IsProjectionPersistenceFile(sample.Source))
            {
                wrongVerdicts.Add($"'{sample.Name}' was not even recognised as projection persistence code");
                continue;
            }

            if (actual != sample)
            {
                wrongVerdicts.Add($"'{sample.Name}': expected {Describe(sample)}, probe said {Describe(actual)}");
            }
        }

        // Assert
        Assert.True(wrongVerdicts.Count == 0,
            "The probe used by Projections_ShouldNeverBeWrittenAsBlindInserts misjudged a sample " +
            "whose verdict is known, so that rule's green means nothing. " +
            $"Misjudged: {string.Join("; ", wrongVerdicts)}.");
    }

    private sealed record ProbeSample(
        string Name, string Source, bool NamesElement, bool DeclaresType, bool AnyWriteSite, bool Blind, bool Stale);

    private static string Describe(ProbeSample s) =>
        $"(namesElement: {s.NamesElement}, declaresType: {s.DeclaresType}, anyWriteSite: " +
        $"{s.AnyWriteSite}, blind: {s.Blind}, stale: {s.Stale})";

    private const string BlindInsertSample = """
        internal sealed class SampleProjectionRepository
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public Task ApplyAsync(SampleProjectionDocument document, CancellationToken cancellationToken) =>
                _projections.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
        """;

    private const string GuardedInsertSample = """
        internal sealed class SampleProjectionRepository
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public async Task ApplyAsync(SampleProjectionDocument document, CancellationToken cancellationToken)
            {
                try
                {
                    await _projections.InsertOneAsync(document, cancellationToken: cancellationToken);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // Another delivery of the same message inserted it first.
                }
            }
        }
        """;

    private const string AnnotatedInsertSample = """
        internal sealed class SampleProjectionRepository
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public Task ApplyAsync(SampleProjectionDocument document, CancellationToken cancellationToken) =>
                // architecture:allow-unguarded-insert: unique index on (listId, itemId) makes the
                // duplicate impossible, and the caller treats the write error as terminal.
                _projections.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
        """;

    private const string UpsertSample = """
        internal sealed class SampleProjectionRepository
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public Task ApplyAsync(SampleProjectionDocument document, CancellationToken cancellationToken) =>
                _projections.ReplaceOneAsync(
                    d => d.Id == document.Id,
                    document,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken);
        }
        """;

    private const string UpdateOnlySample = """
        internal sealed class SampleProjectionRepository
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public Task ApplyAsync(Guid listId, GiftItemProjectionDocument item, CancellationToken cancellationToken) =>
                _projections.UpdateOneAsync(
                    Builders<SampleProjectionDocument>.Filter.Eq(d => d.Id, listId),
                    Builders<SampleProjectionDocument>.Update.Push(d => d.Items, item),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);
        }
        """;

    private const string ReadOnlySample = """
        internal sealed class SampleProjectionReader
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public Task<SampleProjectionDocument> ReadAsync(Guid listId, CancellationToken cancellationToken) =>
                _projections.Find(d => d.Id == listId).FirstOrDefaultAsync(cancellationToken);
        }
        """;

    private const string UnrecognisedSurfaceSample = """
        internal sealed class SampleProjectionRepository
        {
            private readonly IMongoCollection<SampleProjectionDocument> _projections;

            public Task ApplyAsync(SampleProjectionDocument document, CancellationToken cancellationToken) =>
                _projections.SomeDriverApiThisRuleHasNeverHeardOf(document, cancellationToken);
        }
        """;

    private const string ElementOnlySample = """
        internal sealed class GiftListReadModelWriter
        {
            private readonly IMongoCollection<GiftListProjectionDocument> _readModel;

            public Task ApplyAsync(GiftListProjectionDocument document, CancellationToken cancellationToken) =>
                _readModel.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
        """;

    private const string DeclarationOnlySample = """
        internal sealed class ReservationProjectionRepository
        {
            private readonly IMongoCollection<ReservationReadModel> _readModel;

            public Task ApplyAsync(ReservationReadModel document, CancellationToken cancellationToken) =>
                _readModel.ReplaceOneAsync(
                    d => d.Id == document.Id,
                    document,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken);
        }
        """;
}
