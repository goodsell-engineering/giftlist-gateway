using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// Binds CONVENTIONS.md "Messaging"'s blind-insert rule to real files. Per-repo, and deliberately
/// not part of the synced common set: each repo that owns a projection names its own here (this
/// used to say Gateway owned the only projection in the system — false since GL-34 gave
/// Reservations one of its own, and doubly so since GL-38 gave the Gateway a second; GL-115
/// folded the correction into the PRs already touching each copy). Same precedent as
/// <c>Gateway.UnitTests/Architecture/GrpcStatusCodeCastTests.cs</c>. Gateway binds both of its
/// projection repositories: the gift-list one (a versioned compare-and-set loop) and the
/// reservation one (a plain keyed upsert) are the two write shapes this repo actually has, and a
/// detector that recognises one and not the other is exactly the blindness this test exists to
/// catch.
///
/// <para><b>Why this exists.</b> <c>ProjectionWriteRuleTests</c> is two tests, and neither one
/// constrains the detector against production code. The scan skips any file the detector does
/// not recognise and reports only what it finds, so a detector that has gone blind produces an
/// empty result set and a green tick. The probe self-test pins the detector's verdicts against
/// hand-written samples — which constrains it as applied to those samples, and to nothing else.
/// Blanking the write-call pattern entirely used to leave both green: the scan found zero write
/// sites in Gateway's real repository, and the per-file stale guard stayed silent because that
/// file also reads (GL-61 review). Fixtures cannot catch that. A named real file can.</para>
///
/// <para>If a bound file is renamed or moved, update its row here rather than deleting it — a
/// rename is exactly the moment the binding matters most.</para>
/// </summary>
public class ProjectionWriteBindingTests
{
    [Theory]
    [InlineData("src/Gateway.Infrastructure/GiftLists/Persistence/GiftListProjectionRepository.cs")]
    [InlineData("src/Gateway.Infrastructure/Reservations/Persistence/ReservationProjectionRepository.cs")]
    public void TheBlindInsertProbe_ShouldStillSeeTheRealProjectionRepository(string projectionRepositoryPath)
    {
        // Arrange
        var path = Path.Combine(RepoDiscovery.RepoRoot, projectionRepositoryPath.Replace('/', Path.DirectorySeparatorChar));
        var failures = new List<string>();

        // Act
        var text = File.Exists(path) ? File.ReadAllText(path) : null;
        var recognised = text is not null && ProjectionWriteProbe.IsProjectionPersistenceFile(text);
        var sites = text is null ? [] : ProjectionWriteProbe.FindWriteSites(text);

        if (text is null)
        {
            failures.Add(
                $"'{projectionRepositoryPath}' does not exist. If it moved, point this test at its " +
                "new home; the CONVENTIONS.md \"Messaging\" blind-insert rule has no other binding to real code");
        }
        else
        {
            if (!recognised)
            {
                failures.Add(
                    "the detector no longer recognises it as projection persistence code, so the " +
                    "CONVENTIONS.md \"Messaging\" scan silently skips it and passes on an empty population");
            }

            if (sites.Count == 0)
            {
                failures.Add(
                    "the detector found no Mongo write site in it at all, so the CONVENTIONS.md \"Messaging\" scan has " +
                    "nothing to judge and passes vacuously — note the per-file stale guard cannot " +
                    "catch this, because this file also reads");
            }
        }

        // Assert
        Assert.True(failures.Count == 0,
            $"The CONVENTIONS.md \"Messaging\" blind-insert rule has come unbound from '{projectionRepositoryPath}', one of " +
            "this repo's real projections: " + string.Join("; ", failures) +
            $". Detector said (recognised: {recognised}, write sites: {sites.Count}). Fix the " +
            "detector in Support/ProjectionWriteProbe.cs — the rule is not enforcing anything " +
            "against production code until this passes.");
    }
}
