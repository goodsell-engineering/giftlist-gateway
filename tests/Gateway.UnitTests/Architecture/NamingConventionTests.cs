using System.Text.RegularExpressions;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// The CONVENTIONS.md "Naming" rules [AT], mechanised as far as a text scan can reach without a
/// full C# parser. There is deliberately almost no production code to check yet — the point of
/// GL-10 is that these rules are wired in before the first interactor/handler/event is written,
/// not retrofitted after the first violation, so most of these pass vacuously today (zero
/// matches found) and start biting the moment matching code appears.
///
/// "Imperative verb" for input-port/command names is left to review — it requires reading
/// English, not matching a pattern, and no allowlist-plus-opt-out shortcut for it is obvious the
/// way there is for "past tense". "Past tense" for event names *is* partially mechanised below
/// (<see cref="LooksLikePastTenseEventName"/>): every event ARCHITECTURE.md "Event catalogue" plans ends
/// "ed", so an "ed"/"en" suffix check plus a short irregular allowlist and a documented opt-out
/// catches the mistake people actually make (GiftListCreate, or CreateGiftList used where an
/// event name belongs) without attempting to judge English grammar in general.
/// </summary>
public class NamingConventionTests
{
    private static readonly Regex TypeDeclaration =
        new(@"\b(?:class|record|record\s+struct|struct)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);

    /// <summary>
    /// Detects an interactor declared as any struct-flavoured type — <c>struct</c>,
    /// <c>readonly struct</c>, <c>ref struct</c>, <c>record struct</c>, or
    /// <c>readonly record struct</c> — which <see cref="Interactors_ShouldNeverBeDeclaredAsAStruct"/>
    /// treats as a banned form (GL-88), never as a legal shape. CONVENTIONS.md "Use cases" bans
    /// all of "struct, record struct, or readonly record struct" without qualification, and
    /// CS0452 applies identically to every one of them, so the detector requires only the literal
    /// "struct" keyword with its optional modifiers — not just the two record-flavoured forms —
    /// rather than reproduce GL-88's own defect class (a declaration form invisible to the rule)
    /// inside the rule that bans it.
    ///
    /// Deliberately its OWN regex, not a widening of <c>DeferredRuleTests.InteractorDeclaration</c>
    /// or the <c>interactorDeclaration</c> local variable in
    /// <see cref="Interactors_ShouldBeInternalSealed"/> below: GL-85 pinned both of those as
    /// matching only <c>class</c>/<c>record</c>/<c>record class</c>, byte-identical across all
    /// five repos, and GL-88 was raised specifically to protect that byte-identity — widening
    /// either would also have to widen the other or the two files would disagree again about what
    /// an interactor declaration is. A struct interactor cannot ever be a *valid* interactor (see
    /// the ban's rationale on <see cref="Interactors_ShouldNeverBeDeclaredAsAStruct"/>), so
    /// nothing is lost by keeping it out of that shared population and catching it here instead,
    /// with a regex that requires the literal "struct" keyword and therefore cannot also match
    /// the three legal forms.
    /// </summary>
    private static readonly Regex StructInteractorDeclaration =
        new(@"\b(?:readonly\s+)?(?:ref\s+)?(?:record\s+)?struct\s+(?<name>\w*Interactor)\b", RegexOptions.Compiled);

    /// <summary>
    /// Irregular past-tense event verbs that don't end "ed"/"en" — extend this list (or use
    /// <see cref="PastTenseOptOutMarker"/>) rather than loosening the regex in
    /// <see cref="LooksLikePastTenseEventName"/>, which is what actually catches a present-tense
    /// or imperative name used where an event name belongs.
    /// </summary>
    private static readonly string[] IrregularPastTenseEventSuffixes =
    {
        "Sent", "Built", "Left", "Set", "Lost", "Paid",
    };

    /// <summary>
    /// A type whose name is a deliberate, documented exception to the past-tense heuristic (an
    /// irregular verb not in <see cref="IrregularPastTenseEventSuffixes"/>) can carry this
    /// comment on the line immediately above its declaration to opt out.
    /// </summary>
    private const string PastTenseOptOutMarker = "architecture:allow-irregular-event-name";

    private static bool LooksLikePastTenseEventName(string typeName) =>
        Regex.IsMatch(typeName, @"(ed|en)(V\d+)?$") ||
        IrregularPastTenseEventSuffixes.Any(suffix => typeName.EndsWith(suffix, StringComparison.Ordinal));

    private static bool HasPastTenseOptOut(string text, int declarationIndex)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(declarationIndex - 1, 0));
        var precedingLineStart = text.LastIndexOf('\n', Math.Max(lineStart - 1, 0)) + 1;
        var window = text[Math.Max(precedingLineStart, 0)..declarationIndex];
        return window.Contains(PastTenseOptOutMarker, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_ShouldNeverUseBsonAttributes()
    {
        // Arrange
        var domainDirectories = RepoDiscovery.WithRing(ProjectRing.Domain).Select(p => p.Directory).ToList();
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!domainDirectories.Any(d => path.StartsWith(d, StringComparison.Ordinal)))
            {
                continue;
            }

            if (Regex.IsMatch(text, @"\[\s*Bson\w*"))
            {
                offenders.Add(relativePath);
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Domain must never carry [Bson*] attributes — persistence documents are separate " +
            $"types, mapped in Infrastructure (ARCHITECTURE.md \"Data that crosses boundaries\"). Offenders: {string.Join(", ", offenders)}.");
    }

    /// <summary>
    /// The ACL rule (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer"
    /// [AT]): another service's contract types stop at the boundary, and only an Infrastructure
    /// mapper translates them into local models.
    ///
    /// <b>Comments are stripped before the scan, and that is not a loosening.</b> This rule ran
    /// vacuously in the Gateway's repo until GL-25 — <c>ContractsNames()</c> finds a consumed
    /// contracts package by its <c>PackageReference</c>, and before the split GiftLists.Contracts
    /// reached the Gateway as a cross-repo <c>ProjectReference</c>, which that lookup cannot see.
    /// The moment the split turned it into a package the rule woke up and reported five
    /// offenders, every one of them a doc comment on a <c>Record*Request</c> saying which
    /// contract type Infrastructure translated *from* — prose whose whole purpose is to document
    /// that Application does not see it. A rule that fires on a comment explaining the rule is
    /// worse than no rule: the honest fix is to scan code.
    ///
    /// The real test of record for this boundary is
    /// <c>DependencyRuleTests.Application_ClosureShouldExcludeContractsPackages</c>, which reads
    /// the restored package closure and cannot be satisfied by wording. This stays as the cheap
    /// text tripwire that also covers Domain and needs no restore.
    /// </summary>
    [Fact]
    public void DomainAndApplication_ShouldNeverReferenceAContractsType()
    {
        // Arrange
        var innerDirectories = RepoDiscovery.AllProjects
            .Where(p => p.Ring is ProjectRing.Domain or ProjectRing.Application)
            .Select(p => p.Directory)
            .ToList();
        var contractsProjectNames = RepoDiscovery.ContractsNames();
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!innerDirectories.Any(d => path.StartsWith(d, StringComparison.Ordinal)))
            {
                continue;
            }

            var code = WithoutComments(text);
            foreach (var contractsName in contractsProjectNames)
            {
                if (code.Contains($"using {contractsName}", StringComparison.Ordinal) ||
                    Regex.IsMatch(code, $@"\b{Regex.Escape(contractsName)}\."))
                {
                    offenders.Add($"{relativePath} references {contractsName}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Domain and Application must never reference a *.Contracts type — only an " +
            $"Infrastructure mapper may translate at the boundary (CONVENTIONS.md \"Project reference graph\"). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void DomainEvents_ShouldNotCarryAnIntegrationEventVersionSuffix()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!IsUnderDomainEventsFolder(path))
            {
                continue;
            }

            foreach (Match match in TypeDeclaration.Matches(text))
            {
                var typeName = match.Groups[1].Value;
                if (Regex.IsMatch(typeName, @"V\d+$"))
                {
                    offenders.Add($"{relativePath}: {typeName}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "A domain event carries a 'V<n>' suffix — that is the integration-event convention " +
            "(CONVENTIONS.md \"Naming\"; ARCHITECTURE.md \"Domain events are not integration events\"). Domain events never leave "
            + "the process and must not be " +
            $"shaped like the wire contract. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void DomainEvents_ShouldUsePastTenseNaming()
    {
        // Arrange — see LooksLikePastTenseEventName's doc comment for the heuristic and its
        // limits: this catches GiftListCreate/CreateGiftList used where an event name belongs,
        // not every conceivable non-past-tense name.
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!IsUnderDomainEventsFolder(path))
            {
                continue;
            }

            foreach (Match match in TypeDeclaration.Matches(text))
            {
                var typeName = match.Groups[1].Value;
                if (!LooksLikePastTenseEventName(typeName) && !HasPastTenseOptOut(text, match.Index))
                {
                    offenders.Add($"{relativePath}: {typeName}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "A domain event's name doesn't read as past tense (CONVENTIONS.md \"Naming\"). If this is a " +
            $"deliberate irregular verb, add a '// {PastTenseOptOutMarker}' comment on the line " +
            $"above its declaration, or extend IrregularPastTenseEventSuffixes. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void DomainEvents_ShouldLiveInAnEventsFolder()
    {
        // Arrange — CONVENTIONS.md "Folder structure" mandates {Service}.Domain/{Aggregate}/Events/ for domain
        // events, but nothing previously checked it: the version-suffix rule above only ever
        // looks *inside* an Events/ folder, so a misnamed-and-misplaced type — an event moved
        // next to the aggregate root, say, to dodge that rule — was invisible to this suite. This
        // closes that hole with the same past-tense heuristic: anything under Domain that reads
        // like an event by name must actually live in Events/.
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            var inDomain = RepoDiscovery.WithRing(ProjectRing.Domain)
                .Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));
            if (!inDomain || path.Replace('\\', '/').Contains("/Events/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in TypeDeclaration.Matches(text))
            {
                var typeName = match.Groups[1].Value;
                if (LooksLikePastTenseEventName(typeName) && !HasPastTenseOptOut(text, match.Index))
                {
                    offenders.Add($"{relativePath}: {typeName} looks like a domain event but isn't under an Events/ folder");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"CONVENTIONS.md \"Folder structure\": domain events live in {{Service}}.Domain/{{Aggregate}}/Events/. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void IntegrationEvents_ShouldCarryAVersionSuffix()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!IsUnderContractsEventsFolder(path))
            {
                continue;
            }

            foreach (Match match in TypeDeclaration.Matches(text))
            {
                var typeName = match.Groups[1].Value;
                if (!Regex.IsMatch(typeName, @"V\d+$"))
                {
                    offenders.Add($"{relativePath}: {typeName}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "An integration event is missing its 'V<n>' suffix (CONVENTIONS.md \"Naming\"). Add it from " +
            "day one — retrofitting a version suffix later is itself a breaking wire change. " +
            $"Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Interactors_ShouldBeInternalSealed()
    {
        // Arrange
        var applicationDirectories = RepoDiscovery.WithRing(ProjectRing.Application).Select(p => p.Directory).ToList();
        // "record" is included alongside "class" — an interactor is a type with behaviour and
        // injected dependencies, and C# allows `internal sealed record FooInteractor` exactly as
        // validly as `internal sealed class FooInteractor`; the rule means either.
        var interactorDeclaration = new Regex(
            @"^\s*(?<modifiers>(?:public|internal|private|protected|sealed|abstract|partial|static|\s)+)(?:class|record(?:\s+class)?)\s+(?<name>\w*Interactor)\b",
            RegexOptions.Multiline);
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!applicationDirectories.Any(d => path.StartsWith(d, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (Match match in interactorDeclaration.Matches(text))
            {
                var modifiers = match.Groups["modifiers"].Value;
                var isInternal = Regex.IsMatch(modifiers, @"\binternal\b");
                var isSealed = Regex.IsMatch(modifiers, @"\bsealed\b");
                if (!isInternal || !isSealed)
                {
                    offenders.Add($"{relativePath}: {match.Groups["name"].Value} (modifiers: '{modifiers.Trim()}')");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Every interactor must be 'internal sealed' (CONVENTIONS.md \"Use cases\" [AT]). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Interactors_ShouldNeverBeDeclaredAsAStruct()
    {
        // Arrange — CONVENTIONS.md "Use cases" [AT]: services.AddScoped<IInteractor<TRequest,
        // TResponse>, XInteractor>() — the open generic registration every interactor actually
        // uses — does not compile if XInteractor is a value type (error CS0452: "must be a
        // reference type in order to use it as parameter 'TImplementation'"). The only overload a
        // struct *does* compile against is the non-generic
        // AddScoped(typeof(IInteractor<,>), typeof(XInteractor)), which is exactly the shape
        // DeferredRuleTests.IsRegisteredAgainstTheOpenGenericPort already treats as a missing
        // registration. So ANY struct-flavoured interactor — struct, readonly struct, ref
        // struct, record struct, or readonly record struct — is either a compile error or a
        // silent drop out of the Validating<,>/Logging<,> pipeline — never a working interactor
        // (GL-88).
        var applicationDirectories = RepoDiscovery.WithRing(ProjectRing.Application).Select(p => p.Directory).ToList();
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            if (!applicationDirectories.Any(d => path.StartsWith(d, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (Match match in StructInteractorDeclaration.Matches(text))
            {
                offenders.Add($"{relativePath}: {match.Groups["name"].Value}");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "An interactor must never be declared as a 'struct', 'readonly struct', 'ref " +
            "struct', 'record struct', or 'readonly record struct' (CONVENTIONS.md \"Use " +
            "cases\" [AT]) — it cannot be registered against the open generic IInteractor<,> " +
            "port this codebase actually uses (error CS0452), so it is either a compile error " +
            $"or silently dropped from the decorator pipeline. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void TheStructInteractorDetector_ShouldJudgeEveryKnownDeclarationFormCorrectly()
    {
        // Arrange — mirrors
        // DeferredRuleTests.TheInteractorRegistrationProbe_ShouldJudgeKnownRegistrationShapesCorrectly:
        // pins StructInteractorDeclaration's captured group value directly for every banned
        // struct-flavoured form CONVENTIONS.md "Use cases" names — struct, readonly struct, ref
        // struct, record struct, and readonly record struct (GL-88 review finding: a plain
        // `readonly struct` interactor was invisible to the record-struct-only regex this test
        // originally pinned, reproducing GL-88's own defect class inside its fix) — and confirms
        // the three forms Interactors_ShouldBeInternalSealed already treats as legal — class,
        // record, and record class — are NOT matched by this detector. A probe that checked only
        // IsMatch on the struct forms could stay green even if the capture group were broken; a
        // probe that checked only the struct positives could stay green even if the detector
        // also flagged every ordinary interactor as an offender.
        const string structDeclaration =
            "internal sealed struct CreateGiftListInteractor : ICreateGiftList";
        const string readonlyStructDeclaration =
            "internal readonly struct CreateGiftListInteractor : ICreateGiftList";
        const string refStructDeclaration =
            "internal ref struct CreateGiftListInteractor";
        const string recordStructDeclaration =
            "internal sealed record struct CreateGiftListInteractor(IGiftListRepository Lists) : ICreateGiftList;";
        const string readonlyRecordStructDeclaration =
            "internal readonly record struct CreateGiftListInteractor(IGiftListRepository Lists) : ICreateGiftList;";
        const string classDeclaration =
            "internal sealed class CreateGiftListInteractor(IGiftListRepository lists) : ICreateGiftList";
        const string recordDeclaration =
            "internal sealed record CreateGiftListInteractor(IGiftListRepository Lists) : ICreateGiftList;";
        const string recordClassDeclaration =
            "internal sealed record class CreateGiftListInteractor(IGiftListRepository Lists) : ICreateGiftList;";

        // Act
        var findsAStructDeclaration =
            StructInteractorDeclaration.Match(structDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        var findsAReadonlyStructDeclaration =
            StructInteractorDeclaration.Match(readonlyStructDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        var findsARefStructDeclaration =
            StructInteractorDeclaration.Match(refStructDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        var findsARecordStructDeclaration =
            StructInteractorDeclaration.Match(recordStructDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        var findsAReadonlyRecordStructDeclaration =
            StructInteractorDeclaration.Match(readonlyRecordStructDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        var ignoresTheClassForm = !StructInteractorDeclaration.IsMatch(classDeclaration);
        var ignoresTheRecordForm = !StructInteractorDeclaration.IsMatch(recordDeclaration);
        var ignoresTheRecordClassForm = !StructInteractorDeclaration.IsMatch(recordClassDeclaration);

        // Assert
        Assert.True(findsAStructDeclaration,
            "StructInteractorDeclaration failed to find and capture the name of a plain " +
            "'struct' interactor declaration — Interactors_ShouldNeverBeDeclaredAsAStruct " +
            "would let this banned form (GL-88) through unflagged, exactly the gap the GL-88 " +
            "review finding reproduced against the record-struct-only version of this regex.");
        Assert.True(findsAReadonlyStructDeclaration,
            "StructInteractorDeclaration failed to find and capture the name of a 'readonly " +
            "struct' interactor declaration — Interactors_ShouldNeverBeDeclaredAsAStruct would " +
            "let this banned form (GL-88) through unflagged.");
        Assert.True(findsARefStructDeclaration,
            "StructInteractorDeclaration failed to find and capture the name of a 'ref struct' " +
            "interactor declaration — Interactors_ShouldNeverBeDeclaredAsAStruct would let this " +
            "banned form (GL-88) through unflagged.");
        Assert.True(findsARecordStructDeclaration,
            "StructInteractorDeclaration failed to find and capture the name of a 'record " +
            "struct' interactor declaration — Interactors_ShouldNeverBeDeclaredAsAStruct would " +
            "let this banned form (GL-88) through unflagged.");
        Assert.True(findsAReadonlyRecordStructDeclaration,
            "StructInteractorDeclaration failed to find and capture the name of a 'readonly " +
            "record struct' interactor declaration — the form anyone would actually write " +
            "(GL-88) — Interactors_ShouldNeverBeDeclaredAsAStruct would let it through unflagged.");
        Assert.True(ignoresTheClassForm,
            "StructInteractorDeclaration matched an ordinary 'class' interactor declaration — " +
            "Interactors_ShouldNeverBeDeclaredAsAStruct would flag every legal class-form " +
            "interactor as an offender.");
        Assert.True(ignoresTheRecordForm,
            "StructInteractorDeclaration matched an ordinary 'record' interactor declaration — " +
            "Interactors_ShouldNeverBeDeclaredAsAStruct would flag every legal record-form " +
            "interactor as an offender.");
        Assert.True(ignoresTheRecordClassForm,
            "StructInteractorDeclaration matched an ordinary 'record class' interactor " +
            "declaration — Interactors_ShouldNeverBeDeclaredAsAStruct would flag every legal " +
            "record-class-form interactor as an offender.");
    }

    [Fact]
    public void RepositoryPorts_ShouldBeDeclaredInApplication_AndImplementedInInfrastructure()
    {
        // Arrange
        var interfaceDeclaration = new Regex(@"\binterface\s+(I\w*Repository)\b");
        var implementationDeclaration = new Regex(@"\bclass\s+(\w+)\s*:\s*[^{]*\b(I\w*Repository)\b");
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in SourceFiles.AllProductionCode())
        {
            var inApplication = RepoDiscovery.WithRing(ProjectRing.Application)
                .Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));
            var inInfrastructure = RepoDiscovery.WithRing(ProjectRing.Infrastructure)
                .Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));

            if (!inApplication)
            {
                foreach (Match match in interfaceDeclaration.Matches(text))
                {
                    offenders.Add($"{relativePath}: interface {match.Groups[1].Value} must be declared in Application");
                }
            }

            if (!inInfrastructure)
            {
                foreach (Match match in implementationDeclaration.Matches(text))
                {
                    offenders.Add($"{relativePath}: class {match.Groups[1].Value} implementing {match.Groups[2].Value} must be declared in Infrastructure");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Repository port placement is wrong (ARCHITECTURE.md \"The dependency rule is enforced by tests\" item 4 [AT]). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void RepositoryPorts_ShouldReturnAggregatesNeverDocuments()
    {
        // Arrange — CONVENTIONS.md "Persistence" [AT]: "Repositories take and return aggregates, never
        // documents." RepositoryPorts_ShouldBeDeclaredInApplication_AndImplementedInInfrastructure
        // above is a *placement* rule (ARCHITECTURE.md "The dependency rule is enforced by tests" item 4); this is the signature rule,
        // and unlike most of CONVENTIONS.md "Naming" it is cheaply real today — no marker interface or reflection
        // needed, just: does any I*Repository member signature mention Document or BsonDocument.
        var interfaceBlock = new Regex(
            @"interface\s+(?<name>I\w*Repository)\b[^{]*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
            RegexOptions.Singleline);
        var offenders = new List<string>();

        // Act
        foreach (var (_, relativePath, text) in SourceFiles.AllProductionCode())
        {
            foreach (Match match in interfaceBlock.Matches(text))
            {
                var interfaceName = match.Groups["name"].Value;
                var body = match.Groups["body"].Value;
                foreach (var member in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    // "Document" also catches "BsonDocument", "GiftListDocument", etc. — anything
                    // that looks like a persistence document is exactly what must never appear on
                    // a repository port's signature.
                    if (member.Contains("Document", StringComparison.Ordinal))
                    {
                        offenders.Add($"{relativePath}: {interfaceName} -> '{member.Trim()}'");
                    }
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Repositories take and return aggregates, never documents or BsonDocument " +
            $"(CONVENTIONS.md \"Persistence\" [AT]). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void RebusHandlers_ShouldBeNamedAfterTheirMessagePlusHandler()
    {
        // Arrange
        var handlerDeclaration = new Regex(@"\bclass\s+(\w+)\s*:\s*[^{]*\bIHandleMessages<\s*(\w+)\s*>");
        var offenders = new List<string>();

        // Act
        foreach (var (_, relativePath, text) in SourceFiles.AllProductionCode())
        {
            foreach (Match match in handlerDeclaration.Matches(text))
            {
                var className = match.Groups[1].Value;
                var messageType = match.Groups[2].Value;
                var expectedName = $"{messageType}Handler";
                if (!string.Equals(className, expectedName, StringComparison.Ordinal))
                {
                    offenders.Add($"{relativePath}: {className} handles {messageType}, expected to be named {expectedName}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Rebus handler naming is 'Message + Handler' (CONVENTIONS.md \"Naming\"). Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void InputPorts_ShouldHaveAMatchingInteractorAndRequest()
    {
        // Arrange — CONVENTIONS.md "Naming"'s table is a set of paired names, not independent rules:
        // an input port ICreateGiftList implies CreateGiftListInteractor and CreateGiftListRequest
        // both exist. This catches four-file drift (a port added, then its interactor renamed
        // later and the port left behind) without judging whether any one name "reads like" the
        // right part of speech — which is also why I*Repository ports and Common/ ports like
        // IClock, which don't follow this Interactor/Request pattern at all, are excluded below.
        var allFiles = SourceFiles.AllProductionCode().ToList();
        var portDeclaration = new Regex(@"\binterface\s+I(?<useCase>[A-Z]\w*)\b");
        var offenders = new List<string>();

        // Act
        foreach (var (path, relativePath, text) in allFiles)
        {
            var inApplication = RepoDiscovery.WithRing(ProjectRing.Application)
                .Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));
            if (!inApplication || path.Replace('\\', '/').Contains("/Common/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in portDeclaration.Matches(text))
            {
                var useCase = match.Groups["useCase"].Value;
                if (useCase.EndsWith("Repository", StringComparison.Ordinal))
                {
                    continue; // a different pattern entirely — see RepositoryPorts_* above
                }

                var interactorName = $"{useCase}Interactor";
                var requestName = $"{useCase}Request";
                var hasInteractor = allFiles.Any(f =>
                    Regex.IsMatch(f.Text, $@"\b(?:class|record)\s+{Regex.Escape(interactorName)}\b"));
                var hasRequest = allFiles.Any(f =>
                    Regex.IsMatch(f.Text, $@"\b(?:class|record)\s+{Regex.Escape(requestName)}\b"));

                var missing = new List<string>();
                if (!hasInteractor)
                {
                    missing.Add(interactorName);
                }

                if (!hasRequest)
                {
                    missing.Add(requestName);
                }

                if (missing.Count > 0)
                {
                    offenders.Add($"{relativePath}: I{useCase} has no {string.Join(" and no ", missing)}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "CONVENTIONS.md \"Naming\": an input port I{X} implies both {X}Interactor and {X}Request " +
            $"exist. Offenders: {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// Blanks out <c>//</c> line comments and <c>/* */</c> block comments, leaving everything
    /// else — including whitespace and line count — where it was, so an offender's reported
    /// position still means something. String and character literals are tracked, so a <c>//</c>
    /// inside <c>"http://example"</c> does not swallow the rest of the line; verbatim strings
    /// (<c>@"..."</c>) and raw string literals are NOT tracked, which can only make this strip
    /// less than it should, never more, and a contracts reference is not something a string
    /// literal can express anyway.
    ///
    /// Hand-rolled rather than Roslyn: this suite is copied verbatim into five repos
    /// (ArchitectureTestSyncTests) and adding Microsoft.CodeAnalysis to all five UnitTests
    /// projects to delete two comment forms is a poor trade. <see cref="TheCommentStripper_ShouldBlankCommentsAndKeepCode"/> is what keeps the hand-rolled version honest.
    /// </summary>
    private static string WithoutComments(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inChar = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                    result.Append(c);
                }
                else
                {
                    result.Append(' ');
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    result.Append("  ");
                    i++;
                }
                else
                {
                    result.Append(c == '\n' ? c : ' ');
                }

                continue;
            }

            if (inString || inChar)
            {
                result.Append(c);
                if (c == '\\' && next != '\0')
                {
                    result.Append(next);
                    i++;
                }
                else if (inString && c == '"')
                {
                    inString = false;
                }
                else if (inChar && c == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                result.Append("  ");
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                result.Append("  ");
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '\'')
            {
                inChar = true;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// The stripper is the one place this rule can go quietly blind, so its behaviour is pinned
    /// here rather than trusted: strip too little and the rule fires on prose again (GL-25), strip
    /// too much and it stops seeing the <c>using</c> it exists to catch.
    /// </summary>
    [Fact]
    public void TheCommentStripper_ShouldBlankCommentsAndKeepCode()
    {
        // Arrange
        var samples = new (string Name, string Source, string MustNotContain, string MustContain)[]
        {
            ("a doc comment naming a contract type",
             "/// <c>GiftLists.Contracts.Events.GiftListCreatedV1</c>\npublic sealed record R();\n",
             "GiftLists.Contracts.", "public sealed record R();"),
            ("a line comment naming a contract type",
             "// see GiftLists.Contracts.Events\nusing System;\n",
             "GiftLists.Contracts.", "using System;"),
            ("a block comment naming a contract type",
             "/* GiftLists.Contracts.Events */ using System;\n",
             "GiftLists.Contracts.", "using System;"),
            ("a url inside a string literal",
             "var u = \"http://x/GiftLists.Contracts.Y\"; using System;\n",
             "\u0000", "using System;"),
        };
        var judgements = new List<(string Name, string Stripped, string MustNotContain, string MustContain)>();

        // Act
        foreach (var (name, source, mustNotContain, mustContain) in samples)
        {
            judgements.Add((name, WithoutComments(source), mustNotContain, mustContain));
        }

        // Assert
        // The real offender shape first: a using directive must survive, or this rule sees nothing.
        Assert.Contains("using GiftLists.Contracts;", WithoutComments("using GiftLists.Contracts;\n"));
        foreach (var (name, stripped, mustNotContain, mustContain) in judgements)
        {
            Assert.False(stripped.Contains(mustNotContain, StringComparison.Ordinal),
                $"WithoutComments left '{mustNotContain}' in '{name}': {stripped}");
            Assert.True(stripped.Contains(mustContain, StringComparison.Ordinal),
                $"WithoutComments removed code from '{name}': {stripped}");
        }
    }

    private static bool IsUnderDomainEventsFolder(string path)
    {
        var normalized = path.Replace('\\', '/');
        var inDomain = RepoDiscovery.WithRing(ProjectRing.Domain).Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));
        return inDomain && normalized.Contains("/Events/", StringComparison.Ordinal);
    }

    private static bool IsUnderContractsEventsFolder(string path)
    {
        var normalized = path.Replace('\\', '/');
        var inContracts = RepoDiscovery.WithRing(ProjectRing.Contracts).Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));
        return inContracts && normalized.Contains("/Events/", StringComparison.Ordinal);
    }
}
