namespace ArchitectureTests.Support;

/// <summary>
/// Enumerates source as (AbsolutePath, RepoRelativePath, Text) triples, split into the two
/// populations the text-scanning rules care about: production code and test code. The
/// naming-convention rules in <c>NamingConventionTests</c> are regex scans over this rather than
/// reflection over built assemblies, on purpose: most of what CONVENTIONS.md "Naming" names
/// (interactors, domain events, repository ports, Rebus handlers) has no marker interface or
/// attribute to reflect on, and a text scan needs nothing built or restored to run.
///
/// <b>The production/test split is decided here, once, for every rule.</b> GL-81 item (2): the
/// <c>Test</c> ring comes from the <c>.UnitTests</c>/<c>.IntegrationTests</c> name suffixes, so
/// <c>BuildingBlocks.Testing</c> — shared test-support helpers, its own ring since GL-75 — is not
/// in it, and a rule asking "ring != Test" was therefore scanning test-support code as
/// production. That is the mirror image of the exemption GL-75 had to fix in
/// <c>ExceptionAssertionRuleTests</c>, and the same root cause both times: each rule deciding
/// independently what a ring means.
///
/// GL-75's lesson, stated where the next rule's author will read it: <b>adding a value to
/// <see cref="ProjectRing"/> is not a classification change, it is a change to every rule that
/// enumerates rings.</b> So do not enumerate them in a rule. Ask for
/// <see cref="AllProductionCode"/> or <see cref="AllTestCode"/>; if a new ring needs a home, give
/// it one here, in the one place every rule inherits its answer from.
/// </summary>
internal static class SourceFiles
{
    /// <summary>Everything that ships: no test project, and no shared test-support project.</summary>
    public static IEnumerable<(string Path, string RelativePath, string Text)> AllProductionCode() =>
        FilesInProjectsWhere(ring => !IsTestRing(ring));

    /// <summary>
    /// Test projects and the shared test-support project they all reference. A rule about how
    /// tests are written (the <c>Assert.Throws</c> ban) has to cover both halves, or the one
    /// project a shared banned helper would most naturally live in is exempt from the rule
    /// banning it.
    /// </summary>
    public static IEnumerable<(string Path, string RelativePath, string Text)> AllTestCode() =>
        FilesInProjectsWhere(IsTestRing);

    private static bool IsTestRing(ProjectRing ring) =>
        ring is ProjectRing.Test or ProjectRing.BuildingBlocksTesting;

    private static IEnumerable<(string Path, string RelativePath, string Text)> FilesInProjectsWhere(
        Func<ProjectRing, bool> ringPredicate)
    {
        var directories = RepoDiscovery.AllProjects
            .Where(p => ringPredicate(p.Ring))
            .Select(p => p.Directory)
            .Distinct();

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => s is "bin" or "obj"))
                {
                    continue;
                }

                yield return (file, Path.GetRelativePath(RepoDiscovery.RepoRoot, file), File.ReadAllText(file));
            }
        }
    }
}
