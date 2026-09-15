using System.Text.RegularExpressions;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// [AT] rules from CONVENTIONS.md that are not yet mechanically enforced. Each one gets a named,
/// visible <c>Skip</c> — never a silent gap — so "this [AT] marker doesn't actually run a check
/// yet" shows up in every test run instead of only in a document nobody re-reads. Mirrors the
/// GL-53 pattern already in <c>BuildingBlocks.UnitTests/Architecture/ErrorKindTransportMappingTests.cs</c>.
///
/// The remaining two rules below were flagged in review as [AT] markers this suite currently
/// reads as enforced but isn't. A third rule the same review flagged — repositories never
/// returning documents (CONVENTIONS.md "Persistence") — turned out to be cheaply real today and is
/// implemented in <c>NamingConventionTests.RepositoryPorts_ShouldReturnAggregatesNeverDocuments</c>
/// instead of deferred here.
///
/// <para><b>GL-65: interactor registration is no longer one of these.</b> Its Skip said "no
/// service has one yet" and "no Host composition root exists yet to scan" — both false since
/// Identity's Phase 1 composition root and GiftLists' own (GL-20). Worse, once those composition
/// roots existed they turned out to register interactors against the open generic
/// <c>IInteractor&lt;,&gt;</c>, not their named input port as CONVENTIONS.md "Use cases" used to say —
/// deliberately, so one <c>Validating&lt;,&gt;</c>/<c>Logging&lt;,&gt;</c> decorator pair applies
/// uniformly, per every service's own <c>IInteractor</c> doc comment. Un-skipping the rule as
/// originally worded would have failed the architecture, not caught a defect, so CONVENTIONS.md
/// "Use cases" was corrected to describe what the services actually do, and
/// <see cref="Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort"/> now enforces that
/// corrected text — renamed from its original
/// <c>Interactors_ShouldBeRegisteredAgainstTheirInputPort</c> (the identifier Ryan's decision
/// comment used) because that name now reads as asserting the opposite of what the body checks.
/// It stays in this class rather than moving to <c>NamingConventionTests</c>, since it was filed
/// here as a deferred rule and un-skipping it in place is what GL-65 asked for.</para>
/// </summary>
public class DeferredRuleTests
{
    /// <summary>
    /// Matches an interactor's class/record declaration the same way
    /// <c>NamingConventionTests.Interactors_ShouldBeInternalSealed</c> does, minus the modifier
    /// capture that rule needs and this one doesn't — this test only needs the name.
    /// </summary>
    private static readonly Regex InteractorDeclaration =
        new(@"\b(?:class|record(?:\s+class)?)\s+(?<name>\w*Interactor)\b", RegexOptions.Compiled);

    [Fact]
    public void Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort()
    {
        // Arrange — CONVENTIONS.md "Use cases" [AT]: interactors are registered against the open generic
        // IInteractor<TRequest, TResponse> port, not their individual named input port
        // (ICreateGiftList and friends) — see this file's own doc comment and any service's
        // IInteractor.cs for why. The named ports still exist and still compile; nothing ever
        // resolves one from the container, so registering against one instead of IInteractor<,>
        // would silently drop that interactor out of the decorator pipeline (GL-20).
        var allFiles = SourceFiles.AllProductionCode().ToList();
        var interactorNames = allFiles
            .SelectMany(f => InteractorDeclaration.Matches(f.Text).Select(m => m.Groups["name"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var offenders = new List<string>();

        // Act
        foreach (var interactorName in interactorNames)
        {
            if (!IsRegisteredAgainstTheOpenGenericPort(allFiles.Select(f => f.Text), interactorName))
            {
                offenders.Add(interactorName);
            }
        }

        // Assert — a repo that declares no *Interactor at all (BuildingBlocks never will;
        // Reservations doesn't yet) never enters the loop above and offenders stays empty, the
        // same vacuous-when-empty shape NamingConventionTests documents for the same structural
        // reason. That emptiness is not this rule quietly asserting nothing: unlike a plain
        // "did the scan find any match anywhere" check, this loop is driven by the interactors a
        // repo actually declares, so wherever at least one interactor exists (three of five
        // repos today), it must be found registered against IInteractor<,> or it is named here
        // as an offender — it cannot pass by finding zero of something elsewhere. What proves the
        // regex itself hasn't quietly gone stale, independent of any one repo's current content,
        // is TheInteractorRegistrationProbe_ShouldJudgeKnownRegistrationShapesCorrectly below.
        Assert.True(offenders.Count == 0,
            "Missing 'services.AddScoped<IInteractor<TRequest, TResponse>, XInteractor>()' for " +
            "at least one interactor (CONVENTIONS.md \"Use cases\" [AT]) — every interactor must be " +
            "registered against the open generic port, not its named input port, so the " +
            $"Validating<,>/Logging<,> decorator pair actually applies to it. Offenders: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void TheInteractorRegistrationProbe_ShouldJudgeKnownRegistrationShapesCorrectly()
    {
        // Arrange — mirrors ProjectionWriteRuleTests.TheProbe_ShouldJudgeEveryKnownProjectionWriteShapeCorrectly,
        // and covers both moving parts of Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort,
        // not just one: InteractorDeclaration (which interactors exist) and
        // IsRegisteredAgainstTheOpenGenericPort (whether each is registered correctly). A probe
        // that only pinned the second half would still read green if the first broke — an empty
        // InteractorDeclaration match would empty interactorNames, the offender loop above would
        // never run, and the rule would pass vacuously even in GiftLists, Identity and Gateway,
        // which do declare real interactors today. These samples carry their own expected answer
        // and do not depend on any repo's real content, so a future edit to either regex that
        // breaks its half goes red here regardless of which repo runs it.
        const string openGenericRegistration =
            "services.AddScoped<IInteractor<CreateGiftListRequest, CreateGiftListResponse>, CreateGiftListInteractor>();";
        const string namedPortOnlyRegistration =
            "services.AddScoped<ICreateGiftList, CreateGiftListInteractor>();";
        const string realInteractorDeclaration =
            "internal sealed class CreateGiftListInteractor(IGiftListRepository lists) : ICreateGiftList";
        const string recordInteractorDeclaration =
            "internal sealed record CreateGiftListInteractor(IGiftListRepository Lists) : ICreateGiftList;";
        const string namedPortInterfaceDeclaration =
            "public interface ICreateGiftList : IInteractor<A, B>;";

        // Act
        var recognisesTheOpenGenericForm = IsRegisteredAgainstTheOpenGenericPort(
            new[] { openGenericRegistration }, "CreateGiftListInteractor");
        var flagsTheNamedPortOnlyFormAsMissing = !IsRegisteredAgainstTheOpenGenericPort(
            new[] { namedPortOnlyRegistration }, "CreateGiftListInteractor");
        var findsARealDeclaration =
            InteractorDeclaration.Match(realInteractorDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        var findsARealRecordDeclaration =
            InteractorDeclaration.Match(recordInteractorDeclaration).Groups["name"].Value == "CreateGiftListInteractor";
        // Composes the two regexes directly rather than transitively through a shared string
        // literal: the name InteractorDeclaration actually captures from the record declaration
        // is fed straight into the registration probe, the same join
        // Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort itself performs (name
        // discovered -> name searched for). findsARealRecordDeclaration above only proves the
        // capture equals a literal and recognisesTheOpenGenericForm only proves the probe accepts
        // that same literal separately — neither catches a mutation that breaks the hand-off
        // between the two regexes while leaving each half individually correct.
        var recordDeclarationJoinsToItsOwnRegistration = IsRegisteredAgainstTheOpenGenericPort(
            new[] { openGenericRegistration },
            InteractorDeclaration.Match(recordInteractorDeclaration).Groups["name"].Value);
        var ignoresThePortInterface = !InteractorDeclaration.IsMatch(namedPortInterfaceDeclaration);

        // Assert
        Assert.True(recognisesTheOpenGenericForm,
            "The probe failed to recognise a correctly-formed 'AddScoped<IInteractor<,>, " +
            "XInteractor>()' registration — Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort " +
            "would flag every real interactor in every service as an offender.");
        Assert.True(flagsTheNamedPortOnlyFormAsMissing,
            "The probe failed to flag an interactor registered only against its named input " +
            "port, with no IInteractor<,> registration at all — " +
            "Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort would pass even though " +
            "CONVENTIONS.md \"Use cases\"'s decorator pipeline would never reach that interactor.");
        Assert.True(findsARealDeclaration,
            "InteractorDeclaration failed to find a real '*Interactor' class declaration — " +
            "Interactors_ShouldBeRegisteredAgainstTheOpenGenericPort's interactorNames would be " +
            "empty and its offender loop would never run, in every repo, not just the two with " +
            "no interactors today.");
        Assert.True(findsARealRecordDeclaration,
            "InteractorDeclaration failed to find a real '*Interactor' record declaration, or " +
            "matched it without capturing its name — NamingConventionTests.cs already treats " +
            "'record' as a supported interactor shape alongside 'class', so a regex narrowed to " +
            "class-only would empty interactorNames one record-form interactor at a time, with " +
            "no failing test anywhere until that interactor's own registration silently drops " +
            "out of the decorator pipeline.");
        Assert.True(recordDeclarationJoinsToItsOwnRegistration,
            "The name InteractorDeclaration captured from a record-form interactor declaration, " +
            "fed directly into IsRegisteredAgainstTheOpenGenericPort, did not match that same " +
            "interactor's own 'AddScoped<IInteractor<,>, XInteractor>()' registration — the join " +
            "between \"name discovered\" and \"name searched for\" " +
            "(CONVENTIONS.md \"Use cases\" [AT]) is broken for the record-declaration shape even " +
            "though the capture-equals-literal check above may still pass.");
        Assert.True(ignoresThePortInterface,
            "InteractorDeclaration matched a named input port interface declaration (ICreateGiftList : " +
            "IInteractor<,>) as though it were an interactor class — that would have this rule " +
            "check a port interface's own declaration for a registration that will never exist, " +
            "reporting a false offender for every named port in the codebase.");
    }

    /// <summary>
    /// True if any of <paramref name="fileContents"/> registers <paramref name="interactorName"/>
    /// as the implementation half of an <c>AddScoped&lt;IInteractor&lt;TRequest, TResponse&gt;,
    /// XInteractor&gt;()</c> call. Deliberately does not check for the absence of a named-port
    /// registration alongside it — CONVENTIONS.md "Use cases" requires the open-generic registration to
    /// exist, not that the (dead, but harmless) named-port one doesn't.
    /// </summary>
    private static bool IsRegisteredAgainstTheOpenGenericPort(IEnumerable<string> fileContents, string interactorName)
    {
        var registration = new Regex($@"AddScoped<\s*IInteractor<[^<>]*>\s*,\s*{Regex.Escape(interactorName)}\s*>");
        return fileContents.Any(text => registration.IsMatch(text));
    }

    [Fact(Skip =
        "CONVENTIONS.md \"Messaging\" [AT]: 'Projections are written as upserts, never blind inserts.' The " +
        "'never blind inserts' half IS enforced, by " +
        "ProjectionWriteRuleTests.Projections_ShouldNeverBeWrittenAsBlindInserts. This entry is " +
        "the other half — that a projection write is positively an upsert — and it is deferred " +
        "because 'upsert' has several legitimate shapes that only a human can tell apart: a " +
        "plain ReplaceOne/UpdateOne with IsUpsert, a version-filtered compare-and-set replace " +
        "with a retry loop (what Gateway's GiftListProjectionRepository does, and which is NOT " +
        "an upsert by any flag a regex could read), and a read-modify-conditionally-write cycle " +
        "that writes nothing when nothing changed. Flagging every non-IsUpsert write would fail " +
        "all three of Gateway's real projection paths; accepting any write at all would assert " +
        "nothing. Named here so the [AT] marker on that sentence does not read as fully " +
        "mechanised. Revisit if a second service grows a projection and a common shape emerges.")]
    public void Projections_ShouldBeWrittenAsUpserts()
    {
        // Arrange — none

        // Act — none, intentionally: no mechanical definition of "is an upsert" covers the
        // legitimate variants (see Skip reason)

        // Assert
        Assert.Fail("Not implemented — 'is positively an upsert' has no reliable mechanical definition.");
    }

    [Fact(Skip =
        "CONVENTIONS.md \"Messaging\" [AT]: Rebus handlers must contain 'no business logic' — " +
        "deserialize, translate (ACL), call one input port, done. Unlike a naming or " +
        "reference-graph rule, there is no mechanical definition of 'business logic' to " +
        "pattern-match against; the closest available proxy — flagging any handler containing " +
        "an if/for/switch/foreach, or a call into Domain — would misfire on ordinary, harmless " +
        "control flow (a null check before translating, a loop over a batch of items) and train " +
        "people to ignore this suite's red. Left for review, named here rather than silently " +
        "unenforced. Revisit once real handlers exist and a concrete false-positive rate can be " +
        "measured against them.")]
    public void RebusHandlers_ShouldContainNoBusinessLogic()
    {
        // Arrange — none

        // Act — none, intentionally: no mechanical check exists yet (see Skip reason)

        // Assert
        Assert.Fail("Not implemented — no reliable mechanical proxy for 'no business logic' exists yet.");
    }
}
