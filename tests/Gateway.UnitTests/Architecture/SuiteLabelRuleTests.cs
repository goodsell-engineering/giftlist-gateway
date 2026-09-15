using System.Text.RegularExpressions;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// GL-92 [AT]: every container an integration suite starts carries a
/// <c>giftlist.suite</c> label naming that suite, and the name is this project's own
/// (CONVENTIONS.md "Making containerised tests fast enough to keep", added by GL-94 — until
/// then this rule had no counterpart in either canonical document).
///
/// <para><b>The defect this exists to make impossible.</b> Testcontainers computes a container's
/// reuse hash from its configuration, labels included. Identity.IntegrationTests' and
/// GiftLists.IntegrationTests' fixtures were configured identically — <c>mongo:7</c>,
/// <c>rabbitmq:3.13-management</c>, the same builder calls, no distinguishing label — so
/// <c>.WithReuse(true)</c> resolved both to one hash and handed both suites the same pair of
/// containers. Confirmed on a real daemon: two unlabelled containers with one reuse hash each,
/// serving both suites. Each suite's per-test "drop the database" then drops the other's data,
/// and both suites' Rebus hosts bind onto one broker, where a shared queue name round-robins
/// messages between two different services' handlers. The GL-18 review found and fixed exactly
/// this between Gateway and Identity, by adding the first of these labels; nothing carried the
/// fix to the second pair, and nothing noticed until the Phase 2 gate went looking at the
/// daemon itself, seventy-odd issues later.</para>
///
/// <para><b>Why the rule is "the label equals this project's name" and not "the labels differ".</b>
/// A distinctness check is the obvious rule and it is the wrong one: <see cref="RepoDiscovery"/>
/// roots at the nearest <c>*.sln</c>, and there is no solution file above the service directories
/// — only one inside each, which is what becomes one repository at GL-25 (ARCHITECTURE.md
/// "Repository layout"). A rule comparing this repo's suites against each other therefore compares
/// a set of one <em>today</em>, not merely after the split: it passes and pins nothing — which is
/// precisely the GL-88/GL-91 failure of a check that detects less than it documents. Deriving the
/// expected label from the .csproj name instead is checkable inside a single repo, and gives
/// global distinctness for free because two suites with the same label would have to be two
/// projects with the same name.</para>
///
/// <para><b>What it does not reach.</b> A container started through a helper that hides the
/// builder behind a method call, and any container started outside this repo. The
/// "not one expression" fault below is what keeps the first of those loud rather than silent: a
/// builder split across statements is reported as unanalysable, not skipped. Test code outside an
/// integration-test project is scanned too, and starting a container there is itself the
/// violation — there is no suite whose name could label it.</para>
///
/// <para>One more, named because it is invisible from the code: the generic
/// <c>DotNet.Testcontainers.Builders.ContainerBuilder</c> is placed by neither half of
/// <see cref="TestcontainersBuilderTypes"/> — its namespace is <c>DotNet.Testcontainers.*</c>, not
/// the <c>Testcontainers.&lt;Module&gt;</c> a module import gives — so a generic container is seen
/// only when it calls <c>WithReuse</c>. That is the harmful case, so the property this rule exists
/// for survives; what is lost is <c>docker ps</c> attribution for a generic container that is
/// never reused.</para>
///
/// <para>Two tests, because the scan alone would be vacuous in a repo whose integration suite is
/// still empty (Reservations today): the scan over whatever this repo really contains, and
/// <see cref="TheProbe_ShouldJudgeEverySuiteLabelShapeCorrectly"/>, which pins the detector's
/// verdicts against samples that name their own expected answer.</para>
/// </summary>
public class SuiteLabelRuleTests
{
    private const string SuiteLabelKey = "giftlist.suite";
    private const string IntegrationTestsSuffix = ".IntegrationTests";

    /// <summary>
    /// The samples in <see cref="TheProbe_ShouldJudgeEverySuiteLabelShapeCorrectly"/> are container
    /// constructions, and this rule scans every test file in the repo including its own source —
    /// so written out whole they would (correctly) be reported as a unit-test project starting
    /// containers. Splitting the <c>new</c> keyword off the builder type keeps the samples
    /// readable and keeps this file honestly inside the population it scans. An exempted file is
    /// a hole; this rule has none for itself, the same move <see cref="CitationRuleTests"/> makes
    /// with the character it bans.
    /// </summary>
    private const string New = "new ";

    /// <summary>
    /// <c>using Testcontainers.MongoDb;</c> means <c>MongoDbBuilder</c> is a container builder.
    /// Derived per project rather than hard-coded, so adding a module (PostgreSql, Redis) brings
    /// its builder under the rule with no edit here.
    /// </summary>
    private static readonly Regex TestcontainersUsing =
        new(@"using\s+Testcontainers\.(?<module>\w+)\s*;", RegexOptions.Compiled);

    private static readonly Regex BuilderConstruction =
        new(@"new\s+(?<qualifier>Testcontainers\.\w+\.)?(?<type>\w*Builder)\s*\(", RegexOptions.Compiled);

    private static readonly Regex LabelCall =
        new(@"\.WithLabel\(\s*(?<key>""[^""]*""|[\w.]+)\s*,\s*(?<value>""[^""]*""|[\w.]+)\s*\)",
            RegexOptions.Compiled);

    private static readonly Regex StringConstant =
        new(@"const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""\s*;", RegexOptions.Compiled);

    [Fact]
    public void TestContainers_ShouldCarryTheirOwnSuitesLabel_WhenStartedByAnIntegrationSuite()
    {
        // Arrange
        var integrationProjects = RepoDiscovery.AllProjects
            .Where(p => p.Name.EndsWith(IntegrationTestsSuffix, StringComparison.Ordinal))
            .ToList();
        var filesByProject = integrationProjects.ToDictionary(p => p.Name, _ => new List<(string RelativePath, string Text)>());
        var strayFiles = new List<(string RelativePath, string Text)>();
        var offenders = new List<string>();
        var scannedChains = 0;

        foreach (var (path, relativePath, text) in SourceFiles.AllTestCode())
        {
            var owner = integrationProjects.FirstOrDefault(p => IsUnder(path, p.Directory));
            if (owner is null)
            {
                strayFiles.Add((relativePath, text));
            }
            else
            {
                filesByProject[owner.Name].Add((relativePath, text));
            }
        }

        // Act
        foreach (var project in integrationProjects)
        {
            var files = filesByProject[project.Name];
            var expectedLabel = ExpectedLabelFor(project.Name);
            var builderTypes = TestcontainersBuilderTypes(files.Select(f => f.Text));
            var constants = StringConstants(files.Select(f => f.Text));

            foreach (var (relativePath, text) in files)
            {
                foreach (var chain in ContainerChains(text, builderTypes))
                {
                    scannedChains++;
                    var fault = SuiteLabelFault(chain, expectedLabel, constants);
                    if (fault is not null)
                    {
                        offenders.Add($"{relativePath}: {fault}");
                    }
                }
            }
        }

        foreach (var (relativePath, text) in strayFiles)
        {
            var builderTypes = TestcontainersBuilderTypes([text]);
            foreach (var _ in ContainerChains(text, builderTypes))
            {
                scannedChains++;
                offenders.Add(
                    $"{relativePath}: starts a container from outside a *{IntegrationTestsSuffix} project, "
                    + "so there is no suite name to label it with. Containers belong to an integration suite.");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Every container an integration suite starts must carry a \"{SuiteLabelKey}\" label equal to its "
            + "project name without the .IntegrationTests suffix, lower-cased, or Testcontainers' reuse hash "
            + $"resolves two suites to one container (GL-92). Scanned {scannedChains} container construction(s) "
            + $"across {integrationProjects.Count} integration project(s). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void TheProbe_ShouldJudgeEverySuiteLabelShapeCorrectly()
    {
        // Arrange — every shape that has appeared in a fixture or plausibly could, each carrying
        // the verdict it expects, so the detector cannot quietly stop detecting. Written as whole
        // file texts (usings included) because which types count as container builders is itself
        // derived from the usings.
        // Harvested by the same code the scan uses, and judged against a label derived by the same
        // code the scan uses — a probe that hand-builds either only ever pins the two functions in
        // the middle. The reviewer's mutation for this: ExpectedLabelFor returning
        // ToUpperInvariant left Reservations green on both tests, because nothing outside a repo
        // that actually contains a fixture depended on the derivation.
        var constants = StringConstants([
            """
            public const string SuiteLabel = "identity";
            private const string OtherSuiteLabel = "gateway";
            """,
        ]);
        var expectedLabel = ExpectedLabelFor("Identity" + IntegrationTestsSuffix);
        var derivations = new (string ProjectName, string Expected)[]
        {
            ("Identity" + IntegrationTestsSuffix, "identity"),
            // Plural, because the label names a test assembly to a Docker daemon and is derived
            // from the .csproj — not the singular database name of CONVENTIONS.md "Persistence".
            ("GiftLists" + IntegrationTestsSuffix, "giftlists"),
            ("BuildingBlocks" + IntegrationTestsSuffix, "buildingblocks"),
        };
        var samples = new (string Description, string FileText, bool ExpectAFault)[]
        {
            ("literal label, correct value",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithReuse(true).WithLabel("giftlist.suite", "identity").Build();
                """, false),
            ("constant label, correct value",
                $"""
                using Testcontainers.RabbitMq;
                var c = {New}RabbitMqBuilder("rabbitmq:3.13-management")
                    .WithReuse(Reuse)
                    .WithLabel("giftlist.suite", SuiteLabel)
                    .Build();
                """, false),
            ("qualified constant label, correct value",
                $"""
                using Testcontainers.RabbitMq;
                var c = {New}RabbitMqBuilder("rabbitmq:3.13-management")
                    .WithLabel("giftlist.suite", InfrastructureFixture.SuiteLabel)
                    .Build();
                """, false),
            ("a comment carrying a semicolon mid-chain, which used to cut the chain in half",
                $"""
                using Testcontainers.RabbitMq;
                var c = {New}RabbitMqBuilder("rabbitmq:3.13-management")
                    // Reused locally; CI starts clean.
                    .WithReuse(Reuse)
                    .WithLabel("giftlist.suite", SuiteLabel)
                    .Build();
                """, false),
            ("a string argument carrying a semicolon",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7")
                    .WithEnvironment("OPTIONS", "a=1;b=2")
                    .WithLabel("giftlist.suite", "identity")
                    .Build();
                """, false),
            ("the GL-92 defect itself: no label at all",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithReuse(Reuse).Build();
                """, true),
            ("another suite's label, copied with the fixture",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithLabel("giftlist.suite", OtherSuiteLabel).Build();
                """, true),
            ("another suite's label as a literal",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithLabel("giftlist.suite", "gateway").Build();
                """, true),
            ("labelled, but under a different key",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithLabel("giftlist-suite", "identity").Build();
                """, true),
            ("label value that resolves to nothing this project declares",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithLabel("giftlist.suite", SomeUnknownConstant).Build();
                """, true),
            ("builder split across statements, so its labels cannot be read",
                $"""
                using Testcontainers.MongoDb;
                var b = {New}MongoDbBuilder("mongo:7");
                b = b.WithReuse(true);
                var c = b.Build();
                """, true),
            ("a Testcontainers module this repo has not imported, reached fully qualified",
                $"""
                var c = {New}Testcontainers.PostgreSql.PostgreSqlBuilder().Build();
                """, true),
            ("an unimported builder type betrayed by the Testcontainers-only WithReuse call",
                $"""
                var c = {New}PostgreSqlBuilder().WithReuse(true).Build();
                """, true),
        };
        var nonContainerSamples = new (string Description, string FileText)[]
        {
            ("a generic host builder, which starts no container",
                """
                using Microsoft.Extensions.Hosting;
                var builder = Host.CreateApplicationBuilder();
                var host = builder.Build();
                """),
            ("a StringBuilder, which is not a container builder either",
                $"""
                var text = {New}StringBuilder().Append("x").ToString();
                """),
        };
        var misjudged = new List<string>();

        // Act
        foreach (var (description, fileText, expectAFault) in samples)
        {
            var builderTypes = TestcontainersBuilderTypes([fileText]);
            var chains = ContainerChains(fileText, builderTypes);
            if (chains.Count != 1)
            {
                misjudged.Add($"{description}: expected exactly one container construction, found {chains.Count}");
                continue;
            }

            var fault = SuiteLabelFault(chains[0], expectedLabel, constants);
            if (expectAFault != fault is not null)
            {
                misjudged.Add($"{description}: expected a fault? {expectAFault}; got '{fault ?? "no fault"}'");
            }
        }

        foreach (var (projectName, expected) in derivations)
        {
            var derived = ExpectedLabelFor(projectName);
            if (derived != expected)
            {
                misjudged.Add($"the label derivation: {projectName} should give \"{expected}\", gave \"{derived}\"");
            }
        }

        foreach (var declaration in new[] { "SuiteLabel", "OtherSuiteLabel" })
        {
            if (!constants.ContainsKey(declaration))
            {
                misjudged.Add(
                    $"the constant harvester: missed the declaration of {declaration}, so every label written as a "
                    + "constant rather than a literal would be reported unresolvable");
            }
        }

        foreach (var (description, fileText) in nonContainerSamples)
        {
            var builderTypes = TestcontainersBuilderTypes([fileText]);
            var chains = ContainerChains(fileText, builderTypes);
            if (chains.Count != 0)
            {
                misjudged.Add($"{description}: expected no container construction, found {chains.Count}");
            }
        }

        // Assert
        Assert.True(misjudged.Count == 0,
            $"The {SuiteLabelKey} detector judged a known shape wrongly: {string.Join("; ", misjudged)}.");
    }

    /// <summary>
    /// GL-93 review (S4): nothing enforced that a builder actually carried
    /// <c>.WithReliableWaitStrategy()</c> — every current fixture happened to have it, but the
    /// next one silently gets Testcontainers' own defective default back (GL-93's whole hang),
    /// and the symptom reads as a flaky machine, not a missing call. Same shape as GL-91: a check
    /// that would have detected less than this file documents. Reuses <see cref="ContainerChains"/>
    /// rather than re-harvesting, so this rule and the suite-label rule can never disagree about
    /// what counts as a container construction.
    /// </summary>
    [Fact]
    public void TestContainers_ShouldUseTheReliableWaitStrategy_WhenStartedByAnIntegrationSuite()
    {
        // Arrange
        var offenders = new List<string>();
        var scannedChains = 0;

        // Act
        foreach (var (_, relativePath, text) in SourceFiles.AllTestCode())
        {
            var builderTypes = TestcontainersBuilderTypes([text]);
            foreach (var chain in ContainerChains(text, builderTypes))
            {
                scannedChains++;
                if (IsDeadBrokerException(relativePath))
                {
                    continue;
                }

                var fault = ReliableWaitStrategyFault(chain);
                if (fault is not null)
                {
                    offenders.Add($"{relativePath}: {fault}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Every Testcontainers builder an integration suite starts must call "
            + ".WithReliableWaitStrategy() (BuildingBlocks.Testing.ReliableReadiness), or it keeps "
            + "Testcontainers' own default wait strategy, which reads container history instead of "
            + "probing the live server and can hang for up to an hour on a container that has ever "
            + $"been reused and restarted (GL-93). The one named exception is "
            + $"{DeadBrokerRelativePath} — see {nameof(IsDeadBrokerException)}. Scanned {scannedChains} "
            + $"container construction(s). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void TheReliableWaitStrategyProbe_ShouldJudgeEveryShapeCorrectly()
    {
        // Arrange — every shape that has appeared in a fixture or plausibly could, each carrying
        // the verdict it expects, so the detector cannot quietly stop detecting (the same
        // mutation-pin discipline TheProbe_ShouldJudgeEverySuiteLabelShapeCorrectly uses above).
        const string otherFile = "tests/Foo.IntegrationTests/Fixtures/OtherFixture.cs";
        var samples = new (string Description, string FileText, string RelativePath, bool ExpectAFault)[]
        {
            ("carries the reliable wait strategy",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithReuse(true).WithReliableWaitStrategy().Build();
                """, otherFile, false),
            ("the GL-93 defect itself: no reliable wait strategy at all",
                $"""
                using Testcontainers.MongoDb;
                var c = {New}MongoDbBuilder("mongo:7").WithReuse(true).Build();
                """, otherFile, true),
            ("the named dead-broker exception, which is allowed to skip it",
                $"""
                using Testcontainers.RabbitMq;
                var c = {New}RabbitMqBuilder("rabbitmq:3.13-management")
                    .WithLabel("giftlist.suite", InfrastructureFixture.SuiteLabel)
                    .Build();
                """, DeadBrokerRelativePath, false),
            ("the same shape as the dead broker, but in a different file, which is not exempt",
                $"""
                using Testcontainers.RabbitMq;
                var c = {New}RabbitMqBuilder("rabbitmq:3.13-management")
                    .WithLabel("giftlist.suite", InfrastructureFixture.SuiteLabel)
                    .Build();
                """, otherFile, true),
        };
        var misjudged = new List<string>();

        // Act
        foreach (var (description, fileText, relativePath, expectAFault) in samples)
        {
            var builderTypes = TestcontainersBuilderTypes([fileText]);
            var chains = ContainerChains(fileText, builderTypes);
            if (chains.Count != 1)
            {
                misjudged.Add($"{description}: expected exactly one container construction, found {chains.Count}");
                continue;
            }

            var fault = IsDeadBrokerException(relativePath) ? null : ReliableWaitStrategyFault(chains[0]);
            if (expectAFault != fault is not null)
            {
                misjudged.Add($"{description}: expected a fault? {expectAFault}; got '{fault ?? "no fault"}'");
            }
        }

        // Assert
        Assert.True(misjudged.Count == 0,
            $"The reliable-wait-strategy detector judged a known shape wrongly: {string.Join("; ", misjudged)}.");
    }

    /// <summary>
    /// The one container this repo starts that <see cref="TestContainers_ShouldUseTheReliableWaitStrategy_WhenStartedByAnIntegrationSuite"/>
    /// does not require <c>.WithReliableWaitStrategy()</c> from: <c>UserEventPublisherFailureTests</c>'
    /// GL-62 dead broker. It never calls <c>.WithReuse</c> and is started and killed within a
    /// single test as its whole lifecycle — GL-93's defect is a wait strategy corrupted by a
    /// container's history across runs, and a container with no history across no runs cannot
    /// have it. Requiring the probe here would not be wrong, just pointless ceremony on a
    /// container nothing ever reuses. Named here, in the rule, rather than trusted to that file's
    /// own comment alone: this is the check a future exemption would have to convince.
    /// </summary>
    private const string DeadBrokerRelativePath =
        "tests/Identity.IntegrationTests/Users/UserEventPublisherFailureTests.cs";

    private static bool IsDeadBrokerException(string relativePath) =>
        relativePath.Replace(Path.DirectorySeparatorChar, '/') == DeadBrokerRelativePath;

    /// <summary>
    /// Why this container-builder expression breaks the reliable-wait-strategy rule, or
    /// <see langword="null"/> if it does not. A plain substring check, deliberately: the call
    /// this rule polices for is a chosen name in this repo's own code, not an external API this
    /// scan has to allow for multiple spellings of.
    /// </summary>
    private static string? ReliableWaitStrategyFault(string chain) =>
        chain.Contains(".WithReliableWaitStrategy(", StringComparison.Ordinal)
            ? null
            : "starts a container with no .WithReliableWaitStrategy() call, so it keeps Testcontainers' own "
                + $"default wait strategy (GL-93): {Excerpt(chain)}";

    /// <summary>
    /// The suite name a project must label its containers with: the project name without the
    /// <c>.IntegrationTests</c> suffix, lower-cased. Deliberately the project name (so
    /// <c>GiftLists.IntegrationTests</c> gives <c>giftlists</c>, plural) and not the singular
    /// database and queue name of CONVENTIONS.md "Persistence" — this string identifies a test
    /// assembly to a Docker daemon, not a database, and the whole point is that it is derivable
    /// from the .csproj with no table to keep in step.
    /// </summary>
    private static string ExpectedLabelFor(string projectName) =>
        projectName[..^IntegrationTestsSuffix.Length].ToLowerInvariant();

    private static IReadOnlySet<string> TestcontainersBuilderTypes(IEnumerable<string> fileTexts)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in fileTexts)
        {
            foreach (Match match in TestcontainersUsing.Matches(text))
            {
                types.Add(match.Groups["module"].Value + "Builder");
            }
        }

        return types;
    }

    private static IReadOnlyDictionary<string, string> StringConstants(IEnumerable<string> fileTexts)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in fileTexts)
        {
            foreach (Match match in StringConstant.Matches(text))
            {
                constants[match.Groups["name"].Value] = match.Groups["value"].Value;
            }
        }

        return constants;
    }

    /// <summary>
    /// Every container-builder expression in a file, as the text from the construction of a
    /// <c>*Builder</c> up to the end of the statement. A chain counts as a container builder if its type came
    /// from a <c>Testcontainers.*</c> import, if it is written out fully qualified, or if it calls
    /// <c>WithReuse</c> — the last catches a builder whose type this scan could not otherwise
    /// place, which is the one that matters most, since reuse is what makes a missing label
    /// harmful.
    /// </summary>
    internal static IReadOnlyList<string> ContainerChains(string fileText, IReadOnlySet<string> builderTypeNames)
    {
        var chains = new List<string>();
        foreach (Match match in BuilderConstruction.Matches(fileText))
        {
            var end = StatementEnd(fileText, match.Index);
            var chain = end < 0 ? fileText[match.Index..] : fileText[match.Index..end];
            var isContainerBuilder =
                match.Groups["qualifier"].Success
                || builderTypeNames.Contains(match.Groups["type"].Value)
                || chain.Contains(".WithReuse(", StringComparison.Ordinal);
            if (isContainerBuilder)
            {
                chains.Add(chain);
            }
        }

        return chains;
    }

    /// <summary>
    /// Why this one builder expression breaks the rule, or <see langword="null"/> if it does not.
    /// </summary>
    internal static string? SuiteLabelFault(
        string chain, string expectedLabel, IReadOnlyDictionary<string, string> constants)
    {
        if (!chain.Contains(".Build()", StringComparison.Ordinal))
        {
            return "a container is constructed here but not built in the same expression, so this rule cannot "
                + $"read its labels. Keep the builder one expression ending in .Build(): {Excerpt(chain)}";
        }

        foreach (Match label in LabelCall.Matches(chain))
        {
            if (Resolve(label.Groups["key"].Value, constants) != SuiteLabelKey)
            {
                continue;
            }

            var value = Resolve(label.Groups["value"].Value, constants);
            if (value is null)
            {
                return $"labels its container with {label.Groups["value"].Value}, which is not a string literal "
                    + $"and is not a string constant declared in this project: {Excerpt(chain)}";
            }

            return value == expectedLabel
                ? null
                : $"labels its container \"{value}\", but this suite's label is \"{expectedLabel}\" — a container "
                    + $"labelled for another suite is one this suite will share with it: {Excerpt(chain)}";
        }

        return $"starts a container with no \"{SuiteLabelKey}\" label, so its reuse hash is whatever every other "
            + $"identically-configured suite computes and .WithReuse hands them one container: {Excerpt(chain)}";
    }

    /// <summary>
    /// Index of the semicolon that ends the statement beginning at <paramref name="start"/>, or
    /// -1 if there is none. Semicolons inside comments, strings and char literals do not count:
    /// <c>BuildingBlocks.IntegrationTests</c>' broker is configured underneath a <c>//</c> comment
    /// containing one, and a plain <c>IndexOf(';')</c> cut the chain in half there and reported a
    /// correctly labelled container as unanalysable. A false "unanalysable" is as bad as a missed
    /// violation: it teaches whoever hits it that the rule is noise.
    /// </summary>
    private static int StatementEnd(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            switch (c)
            {
                case '/' when next == '/':
                    i = text.IndexOf('\n', i);
                    break;
                case '/' when next == '*':
                    i = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = i < 0 ? -1 : i + 1;
                    break;
                case '@' when next == '"':
                    i = EndOfVerbatimString(text, i + 1);
                    break;
                case '"':
                    i = EndOfString(text, i);
                    break;
                case '\'':
                    i = EndOfCharLiteral(text, i);
                    break;
                case ';':
                    return i;
                default:
                    continue;
            }

            if (i < 0)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>Index of the closing quote of the string starting at <paramref name="quote"/>.</summary>
    private static int EndOfString(string text, int quote)
    {
        var fence = 0;
        while (quote + fence < text.Length && text[quote + fence] == '"')
        {
            fence++;
        }

        if (fence >= 3)
        {
            var closing = text.IndexOf(new string('"', fence), quote + fence, StringComparison.Ordinal);
            return closing < 0 ? -1 : closing + fence - 1;
        }

        if (fence == 2)
        {
            return quote + 1;
        }

        for (var i = quote + 1; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    break;
                case '"':
                    return i;
                case '\n':
                    return -1;
            }
        }

        return -1;
    }

    private static int EndOfVerbatimString(string text, int quote)
    {
        for (var i = quote + 1; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    private static int EndOfCharLiteral(string text, int quote)
    {
        for (var i = quote + 1; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    break;
                case '\'':
                    return i;
                case '\n':
                    return -1;
            }
        }

        return -1;
    }

    private static string? Resolve(string argument, IReadOnlyDictionary<string, string> constants)
    {
        if (argument.StartsWith('"'))
        {
            return argument.Trim('"');
        }

        var lastSegment = argument[(argument.LastIndexOf('.') + 1)..];
        return constants.TryGetValue(lastSegment, out var value) ? value : null;
    }

    private static string Excerpt(string chain)
    {
        var collapsed = string.Join(' ', chain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 160 ? collapsed : collapsed[..160] + "...";
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
