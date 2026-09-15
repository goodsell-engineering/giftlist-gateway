using System.Text;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// GL-83 [AT]: source in this repository never cites a document section by <em>number</em>.
/// A comment names the document and the heading text — <c>CONVENTIONS.md "Use cases"</c>, never
/// <c>CONVENTIONS.md</c> followed by a section number.
///
/// The number is the part that rots, and being document-qualified does not save it. Translating
/// this repo's 759 section citations into headings turned up <b>118 of them, across 86 authored
/// sites — about one citation in six — that had been pointing at the wrong section all
/// along</b>. Only 13 were danglers (a number no document defines, findable by script). The
/// other 105 named a real section that simply was not the one meant, and had survived twenty
/// batches: "CONVENTIONS.md" plus a number is unfalsifiable on a skim, because checking it means
/// opening the document and counting headings.
///
/// Three, all real, all pre-dating this rule: the rule that Domain and Application may not
/// reference a Contracts type was cited as Persistence in five synced copies of
/// DependencyRuleTests, with its own target sentence quoted directly underneath (it is the
/// reference graph); the repository-signature rule was cited as Testing (it is Persistence); and
/// a runtime exception message shipped a citation for a domain invariant that pointed at the
/// dependency-rule section. A heading cannot be silently right-shifted by an insertion three
/// sections earlier, it carries its own meaning to a reader who never opens the document, and —
/// the part that actually mattered here — a reader can tell when it is wrong.
///
/// The corollary, learned the hard way while writing this: <b>translating a citation is not
/// mechanical.</b> Read the cited section itself, not the conventions skill's condensation of
/// it — the skill groups rules under headings the document does not use, and judging against it
/// moved a dozen citations from one wrong heading to another before anyone noticed.
///
/// This is the GL-77 move applied a second time: the defect is made unrepresentable rather than
/// detected. There is nothing left to keep in sync, so nothing to drift.
///
/// **This file contains no section sign, and does not need to exempt itself.** The banned
/// character is written as a <c>\u00A7</c> escape and named in prose as U+00A7 in every
/// message below, so the rule scans its own source like any other file. An exempted file is a
/// hole; this rule has none for itself.
///
/// What it does NOT reach, stated so nobody mistakes green here for a repo-wide guarantee:
/// <list type="bullet">
/// <item>Anything outside this repo's own root (<see cref="RepoDiscovery.RepoRoot"/>, the
/// nearest ancestor holding a <c>*.sln</c>, which pre-split is one service directory). That
/// leaves <c>web/</c>, <c>devenv/</c>, the top-level <c>scripts/</c> <b>and the monorepo root
/// itself</b> — <c>.dockerignore</c>, <c>Directory.Build.props</c>, <c>global.json</c>,
/// <c>README.md</c>, <c>.gitignore</c> — outside every copy of this rule.
/// <c>.dockerignore</c> already carries a citation, so this is a live gap, not a theoretical
/// one. <c>web/</c> has its own equivalent in <c>web/citations.test.ts</c> — a
/// hand-maintained twin, not a synced copy (different language, and a different repo after
/// GL-25), so <see cref="GeneratedMarkers"/> is duplicated there verbatim and both probes
/// iterate it; <c>scripts/sync-arch-tests.sh</c>'s header records what must stay in step and
/// what deliberately differs. The other three become the devenv repo's and the workspace's
/// problem at the GL-25 split; until then they are review's job, and saying so here is the only
/// thing standing between that and "the build is green, so the repo is clean".</item>
/// <item>Generated output, excluded by <see cref="IsGenerated"/> below.</item>
/// <item>Whether a heading citation still <em>resolves</em>. This rule kills the number; it does
/// not verify the words. Nothing here can — the documents live in another repository
/// (ARCHITECTURE.md "Repository layout").</item>
/// </list>
/// </summary>
public class CitationRuleTests
{
    private const char SectionSign = '\u00A7';

    /// <summary>Never walked: build output, package caches, and VCS/editor metadata.</summary>
    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "node_modules", ".git", "dist", "TestResults", ".vs", ".idea",
    };

    /// <summary>
    /// Big enough for anything hand-written here; a cheap guard against reading a large asset
    /// into memory line by line.
    /// </summary>
    private const long MaxFileBytes = 1024 * 1024;

    /// <summary>
    /// How a file declares itself generated. Duplicated verbatim in <c>web/citations.test.ts</c>,
    /// which no script can sync into this one; both probes iterate this list rather than naming
    /// markers inline, so a marker added to one twin and not the other is a visible diff in a
    /// file whose whole job is to say what "generated" means.
    /// </summary>
    private static readonly string[] GeneratedMarkers = { "@generated", "<auto-generated" };

    [Fact]
    public void Source_ShouldNeverCiteADocumentSectionByNumber()
    {
        // Arrange
        var scanned = TextFilesUnderRepoRoot().ToList();
        var offenders = new List<string>();

        // Act
        foreach (var (relativePath, text) in scanned)
        {
            var index = text.IndexOf(SectionSign);
            if (index < 0)
            {
                continue;
            }

            var line = text.Take(index).Count(c => c == '\n') + 1;
            offenders.Add($"{relativePath}:{line}");
        }

        // Assert
        // The population half first: a walk that reaches nothing passes this rule vacuously, and
        // would look exactly like a clean repo. These three pin that it reaches nested test
        // source, production source and project files respectively.
        Assert.True(scanned.Count >= 20,
            $"Only {scanned.Count} text files were scanned under '{RepoDiscovery.RepoRoot}'. Every " +
            "repo in this project has far more than that, so the walk is broken and this rule is " +
            "passing vacuously.");
        Assert.Contains(scanned, f => f.RelativePath.EndsWith(
            $"Architecture/{nameof(CitationRuleTests)}.cs", StringComparison.Ordinal));
        Assert.Contains(scanned, f =>
            f.RelativePath.StartsWith("src/", StringComparison.Ordinal) &&
            f.RelativePath.EndsWith(".cs", StringComparison.Ordinal));
        Assert.Contains(scanned, f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        Assert.True(offenders.Count == 0,
            "U+00A7 SECTION SIGN appears in source (GL-83 [AT]). Cite the document and the " +
            "heading text instead of a section number — CONVENTIONS.md \"Use cases\", not " +
            "CONVENTIONS.md followed by a number: the number rots silently and is copied outward, " +
            $"the heading does not. Offenders: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void TheGeneratedFileFilter_ShouldJudgeKnownHeadersCorrectly()
    {
        // Arrange
        // The exclusion above is the one hole in this rule, so what counts as "generated" is
        // pinned here rather than left to a path convention that a future generator's output
        // directory would quietly fall outside of. Both real generators in this project announce
        // themselves in their first lines: protoc-gen-es writes "@generated by protoc-gen-es",
        // and the .NET tooling convention is an <auto-generated> banner.
        var handWritten =
            "using ArchitectureTests.Support;\n\nnamespace ArchitectureTests;\n";
        var markerBelowTheHeader =
            string.Concat(Enumerable.Repeat("// a hand-written file, line after line\n", 20)) +
            "// ...which happens to say @generated far below its header.\n";

        // Act
        var judgements = GeneratedMarkers
            .Select(marker => (
                Sample: $"a header carrying '{marker}'",
                Generated: IsGenerated($"// {marker} by some tool\n// line two\n"),
                Expected: true))
            .Append((Sample: "hand-written source", Generated: IsGenerated(handWritten), Expected: false))
            .Append((Sample: "a marker below the header", Generated: IsGenerated(markerBelowTheHeader), Expected: false))
            .ToArray();

        // Assert
        // The list itself first: emptied or edited down, every judgement below still passes while
        // the exclusion silently stops matching (or starts matching everything). These two markers
        // are also spelled out in web/citations.test.ts, its hand-maintained twin.
        Assert.Equal(new[] { "@generated", "<auto-generated" }, GeneratedMarkers);

        foreach (var (sample, generated, expected) in judgements)
        {
            Assert.True(generated == expected,
                $"IsGenerated judged '{sample}' as generated={generated}, expected {expected}. " +
                "Too broad and this rule exempts hand-written source; too narrow and it goes red " +
                "the next time a generator runs, which is how a rule gets deleted.");
        }
    }

    /// <summary>
    /// A file is generated if it says so in its own first lines. Path-based detection was
    /// rejected: <c>web/src/gen/</c> is where today's generator happens to write, and a rule
    /// keyed on that silently stops covering the next one.
    /// </summary>
    private static bool IsGenerated(string text)
    {
        var header = text.Split('\n').Take(5).ToList();
        return GeneratedMarkers.Any(marker =>
            header.Any(line => line.Contains(marker, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Every hand-written text file under this repo's root. "Text" is decided by decoding as
    /// strict UTF-8 rather than by an extension allowlist — an allowlist is a list to forget to
    /// add to, and the whole point of this rule is that the next file kind is covered too.
    /// </summary>
    private static IEnumerable<(string RelativePath, string Text)> TextFilesUnderRepoRoot()
    {
        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var file in Directory.EnumerateFiles(
                     RepoDiscovery.RepoRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(RepoDiscovery.RepoRoot, file).Replace('\\', '/');
            if (relativePath.Split('/').Any(segment => SkippedDirectories.Contains(segment)))
            {
                continue;
            }

            if (new FileInfo(file).Length > MaxFileBytes)
            {
                continue;
            }

            string text;
            try
            {
                text = strictUtf8.GetString(File.ReadAllBytes(file));
            }
            catch (DecoderFallbackException)
            {
                continue; // not text at all
            }

            if (IsGenerated(text))
            {
                continue;
            }

            yield return (relativePath, text);
        }
    }
}
