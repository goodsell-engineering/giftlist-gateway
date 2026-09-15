using System.Reflection;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// Enforces the reference graph in CONVENTIONS.md "Project reference graph" [AT]. Every rule here is checked twice,
/// deliberately:
///
/// 1. On the restored package *closure* (obj/project.assets.json) — the test of record. A
///    Cecil/NetArchTest-style check only sees types a project actually *uses*, so it passes
///    right up until someone writes the violation. Asserting on the closure means a driver type
///    is not even reachable, which is the guarantee the convention actually asks for.
/// 2. On the raw ProjectReference/PackageReference elements in the .csproj, and (for
///    Application) on the emitted assembly's AssemblyRef table — cheap second tripwires that
///    don't need a restore to run.
///
/// Discovery is driven entirely by <see cref="RepoDiscovery"/>, which classifies every .csproj
/// under this repo by name. Nothing here hard-codes a project name, so this file is identical —
/// byte for byte — across every service repo (GL-10 report has the detail); it enforces whatever
/// this repo happens to contain, including nothing at all for a ring the service doesn't have
/// (the Gateway has no Domain, per ARCHITECTURE.md "Mapping to Clean Architecture's rings", and
/// owns no *.Contracts either — it publishes no event and accepts no command, so it has no wire
/// surface of that kind to own).
///
/// Each rule is a single [Fact] that iterates its own set of matching projects and aggregates
/// every offender into one assertion, rather than an xUnit [Theory]/[MemberData] pair — a
/// [Theory] with a data source that resolves to zero rows (e.g. Contracts, before any service
/// has one) is a hard xUnit failure ("No data found"), which is exactly the wrong failure mode
/// for a rule that is legitimately vacuous today.
/// </summary>
public class DependencyRuleTests
{
    private static readonly string[] BannedApplicationPackagePrefixes =
    {
        "Rebus",
        "MongoDB",
        "RabbitMQ",
        "HotChocolate",
        "Grpc",
        "Microsoft.AspNetCore",
    };

    private const string BuildingBlocksInfrastructureName = "BuildingBlocks.Infrastructure";

    [Fact]
    public void RepoDiscovery_ShouldFindAtLeastOneProject_InThisRepo()
    {
        // Arrange — none

        // Act
        var count = RepoDiscovery.AllProjects.Count;

        // Assert
        Assert.True(count > 0,
            $"No .csproj files were discovered under '{RepoDiscovery.RepoRoot}'. The discovery " +
            "heuristic (walk up to the nearest *.sln) is probably broken, which means every " +
            "other rule in this file is silently running against an empty set.");
    }

    [Fact]
    public void EveryProject_ShouldClassifyIntoAKnownRing()
    {
        // Arrange — none

        // Act
        var unknown = RepoDiscovery.AllProjects
            .Where(p => p.Ring == ProjectRing.Unknown)
            .Select(p => p.Name)
            .ToList();

        // Assert
        Assert.True(unknown.Count == 0,
            "RepoDiscovery classifies every .csproj purely by name; a project whose name matches " +
            "no known ring suffix silently drops out of every rule in this whole suite instead of " +
            "failing loudly (e.g. renaming GiftLists.Application to GiftLists.UseCases would make " +
            "every Application rule pass vacuously with zero signal). Rename the project to match " +
            $"a ring, or teach ProjectFile.Classify about the new shape. Unclassified: {string.Join(", ", unknown)}.");
    }

    [Fact]
    public void RepoDiscovery_ShouldFindItsExpectedHostAndInfrastructureProjects()
    {
        // Arrange — every service repo has exactly one Host and one Infrastructure project.
        // BuildingBlocks' repo root has neither: it ships BuildingBlocksCore and
        // BuildingBlocksInfrastructure instead, which is how we recognise it here without
        // hard-coding a repo name.
        var isBuildingBlocksRepo = RepoDiscovery.WithRing(ProjectRing.BuildingBlocksCore).Any();

        // Act
        var hostCount = RepoDiscovery.WithRing(ProjectRing.Host).Count();
        var infrastructureCount = RepoDiscovery.WithRing(ProjectRing.Infrastructure).Count();

        // Assert
        if (isBuildingBlocksRepo)
        {
            Assert.True(hostCount == 0 && infrastructureCount == 0,
                "This looks like the BuildingBlocks repo (it has a BuildingBlocksCore project) " +
                $"but also has {hostCount} Host and {infrastructureCount} Infrastructure " +
                "project(s) — the exemption below no longer matches reality.");
            return;
        }

        Assert.True(hostCount >= 1,
            "This repo should discover at least one .Host project. If it genuinely has none, " +
            "adjust this floor to match; if it has one and this still fails, discovery/naming " +
            "classification is broken and every Host rule in this suite is silently vacuous.");
        Assert.True(infrastructureCount >= 1,
            "This repo should discover at least one .Infrastructure project — see the .Host " +
            "assertion above for why this matters.");
    }

    [Fact]
    public void DomainAndBuildingBlocksCore_ShouldHaveBclOnlyClosure()
    {
        // Arrange
        var projects = RepoDiscovery.AllProjects
            .Where(p => p.Ring is ProjectRing.Domain or ProjectRing.BuildingBlocksCore);
        var offenders = new List<string>();

        // Act
        foreach (var project in projects)
        {
            var closure = ProjectAssets.PackageClosure(project);
            if (closure.Count > 0)
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", closure.Select(c => c.Name))}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Domain and BuildingBlocks must reference nothing outside the BCL. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void DomainAndBuildingBlocksCore_ShouldDeclareNoProjectOrPackageReferences()
    {
        // Arrange
        var projects = RepoDiscovery.AllProjects
            .Where(p => p.Ring is ProjectRing.Domain or ProjectRing.BuildingBlocksCore);
        var offenders = new List<string>();

        // Act
        foreach (var project in projects)
        {
            var projectReferences = project.ProjectReferenceNames();
            var packageReferences = project.PackageReferenceIds();
            if (projectReferences.Count > 0 || packageReferences.Count > 0)
            {
                offenders.Add(
                    $"{project.Name} -> ProjectReferences=[{string.Join(", ", projectReferences)}], " +
                    $"PackageReferences=[{string.Join(", ", packageReferences)}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Domain and BuildingBlocks must reference nothing. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Domain_ShouldNotReferenceBuildingBlocks()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Domain))
        {
            var references = project.ProjectReferenceNames();
            if (references.Contains("BuildingBlocks") || references.Contains(BuildingBlocksInfrastructureName))
            {
                offenders.Add(project.Name);
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Domain must not reference BuildingBlocks — Result<T> is an Application-ring concern; " +
            $"aggregates throw on invariant breach instead. Offenders: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void Application_ClosureShouldExcludeDriversAndFrameworks()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Application))
        {
            var closure = ProjectAssets.PackageClosure(project);
            var violations = closure
                .Where(c => BannedApplicationPackagePrefixes.Any(prefix =>
                        c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(c.Name, BuildingBlocksInfrastructureName, StringComparison.Ordinal))
                .Select(c => c.Name)
                .ToList();
            if (violations.Count > 0)
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", violations)}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Application's restored package closure must not make any driver package reachable. " +
            "Checked on the restored closure (obj/project.assets.json), not emitted assembly " +
            "references — a type only has to be reachable, not actually used, for this boundary " +
            $"to be broken. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Application_ClosureShouldExcludeContractsPackages()
    {
        // Arrange — CONVENTIONS.md "Project reference graph" [AT]: "never reference a contracts type from
        // Domain/Application." NamingConventionTests' text scan is the primary check for that;
        // this is the same closure-over-text-scan lesson GL-10 already proved for driver
        // packages (MongoDB.Driver was reachable with zero trace in the compiled DLL) applied to
        // Contracts — a *.Contracts package pulled straight into Application would never show up
        // in an emitted AssemblyRef if nothing in the (largely empty, today) project actually
        // uses a type from it, but it would always show up in the restored closure.
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Application))
        {
            var closure = ProjectAssets.PackageClosure(project);
            var violations = closure
                .Where(c => c.Name.EndsWith(".Contracts", StringComparison.Ordinal))
                .Select(c => c.Name)
                .ToList();
            if (violations.Count > 0)
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", violations)}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Application's restored package closure must not make any *.Contracts package reachable. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Application_ProjectReferencesShouldBeLimitedToOwnDomainAndBuildingBlocks()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Application))
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "BuildingBlocks" };
            var ownDomainName = project.Name.Replace(".Application", ".Domain", StringComparison.Ordinal);
            if (RepoDiscovery.WithRing(ProjectRing.Domain).Any(d => d.Name == ownDomainName))
            {
                allowed.Add(ownDomainName);
            }

            foreach (var reference in project.ProjectReferenceNames())
            {
                if (!allowed.Contains(reference))
                {
                    offenders.Add($"{project.Name} -> {reference}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Application references Domain and BuildingBlocks only — wanting anything else means " +
            $"a port is missing. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Application_EmittedAssemblyShouldNotReferenceDriverAssemblies()
    {
        // Arrange — a cheap second tripwire alongside the closure test above; a project whose
        // assembly hasn't been built yet simply has nothing to check.
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Application))
        {
            var assemblyPath = FindBuiltAssembly(project);
            if (assemblyPath is null)
            {
                continue;
            }

            var assembly = Assembly.LoadFrom(assemblyPath);
            var violations = assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => BannedApplicationPackagePrefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (violations.Count > 0)
            {
                offenders.Add($"{project.Name}.dll -> [{string.Join(", ", violations)}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Application's AssemblyRef table must not contain a driver assembly. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Infrastructure_ProjectReferencesShouldBeLimitedToItsOwnRingAndContracts()
    {
        // Arrange
        var offenders = new List<string>();
        var allowedRings = new[] { ProjectRing.Application, ProjectRing.Domain, ProjectRing.Contracts };
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "BuildingBlocks",
            BuildingBlocksInfrastructureName,
        };
        foreach (var candidate in RepoDiscovery.AllProjects.Where(p => allowedRings.Contains(p.Ring)))
        {
            allowed.Add(candidate.Name);
        }

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Infrastructure))
        {
            foreach (var (name, include) in project.ProjectReferences())
            {
                // CONVENTIONS.md "Project reference graph": Infrastructure may reference "own
                // *.Contracts, other services' *.Contracts". Since GL-25 the second kind arrives
                // as a PackageReference — there is no path from one repo to another — so this
                // loop sees only the local one, and the package half of that permission is
                // checked by RepoDiscovery.ContractsNames() feeding NamingConventionTests'
                // ACL rule instead. The resolution below is kept for the case a ProjectReference
                // to a Contracts project outside this repo's *.sln reappears (a nested clone, a
                // temporary path during a migration): it reclassifies the resolved file with the
                // same Classify() every other rule uses rather than trusting the name suffix,
                // because a bare suffix match would also wave through a typo'd or dangling
                // relative path that ends in ".Contracts" and resolves to nothing on disk.
                var isAllowedContracts = !allowed.Contains(name) && IsResolvedContractsReference(project, include);
                if ((!allowed.Contains(name) && !isAllowedContracts) ||
                    name.EndsWith(".Host", StringComparison.Ordinal))
                {
                    offenders.Add($"{project.Name} -> {name}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Infrastructure may reference its own Application/Domain, its own or another " +
            $"service's *.Contracts, and BuildingBlocks(.Infrastructure) only. Offenders: {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// Resolves a <c>ProjectReference</c>'s raw <c>Include</c> against the referencing project's
    /// own directory and reports whether that path is an actual, on-disk project that classifies
    /// as <see cref="ProjectRing.Contracts"/> — the check
    /// <see cref="Infrastructure_ProjectReferencesShouldBeLimitedToItsOwnRingAndContracts"/> needs
    /// for a reference RepoDiscovery's own walk can never see (it lives in another repo,
    /// pre-Phase-2.5).
    /// </summary>
    private static bool IsResolvedContractsReference(ProjectFile project, string include)
    {
        var resolvedPath = Path.GetFullPath(
            Path.Combine(project.Directory, include.Replace('\\', Path.DirectorySeparatorChar)));
        return ProjectFile.LoadIfExists(resolvedPath) is { Ring: ProjectRing.Contracts };
    }

    [Fact]
    public void Host_ProjectReferencesShouldBeItsOwnInfrastructureAndBuildingBlocks()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Host))
        {
            var servicePrefix = project.Name[..^".Host".Length];
            var expectedInfrastructure = servicePrefix + ".Infrastructure";

            // CONVENTIONS "Project reference graph": Host → Infrastructure, BuildingBlocks, BuildingBlocks.Infrastructure.
            // The composition root is where the building-blocks DI extensions (AddBuildingBlocksRebus,
            // AddBuildingBlocksMongo, AddBuildingBlocksHealthChecks) are called, so it references them
            // directly. It may reference nothing else — no other service, no Application, no Domain.
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                expectedInfrastructure,
                "BuildingBlocks",
                "BuildingBlocks.Infrastructure",
            };

            var references = project.ProjectReferenceNames();
            if (!references.Contains(expectedInfrastructure) || references.Any(r => !allowed.Contains(r)))
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", references)}] (expected {expectedInfrastructure}, optionally plus BuildingBlocks and BuildingBlocks.Infrastructure)");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"Host is wiring only: its own Infrastructure plus the building-blocks projects it composes, nothing else. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void Contracts_ShouldNeverReferenceDomainOrBuildingBlocks()
    {
        // Arrange
        var forbidden = RepoDiscovery.AllProjects
            .Where(p => p.Ring is ProjectRing.Domain or ProjectRing.BuildingBlocksCore or ProjectRing.BuildingBlocksInfrastructure or ProjectRing.BuildingBlocksTesting)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Contracts))
        {
            foreach (var reference in project.ProjectReferenceNames())
            {
                if (forbidden.Contains(reference))
                {
                    offenders.Add($"{project.Name} -> {reference}");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Contracts must never reference Domain or BuildingBlocks, or the innermost ring " +
            $"becomes a published dependency of other services. Offenders: {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// CONVENTIONS.md "Project reference graph" gives Contracts no outgoing edges at all. Before
    /// the GL-25 split, only a ProjectReference could reach anything, so the rule above was the
    /// whole check. Now that BuildingBlocks and every other service's contracts arrive as
    /// packages, a PackageReference is the way this boundary would actually be crossed — and the
    /// rule above cannot see one. Contracts is the service's entire published wire surface
    /// (ARCHITECTURE.md "Contracts: each service owns and publishes its own"), so a dependency
    /// added here becomes a transitive dependency of every consumer of that surface.
    /// </summary>
    [Fact]
    public void Contracts_ShouldDeclareNoPackageReferences()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.Contracts))
        {
            var packageReferences = project.PackageReferenceIds();
            if (packageReferences.Count > 0)
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", packageReferences)}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Contracts references nothing — not Domain, not BuildingBlocks, and no NuGet package " +
            $"either. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void BuildingBlocksInfrastructure_ShouldOnlyReferenceBuildingBlocksCore()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.BuildingBlocksInfrastructure))
        {
            var references = project.ProjectReferenceNames();
            if (references.Count != 1 || references[0] != "BuildingBlocks")
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", references)}] (expected exactly [BuildingBlocks])");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    /// <summary>
    /// GL-75 created BuildingBlocks.Testing to end four copies of a queue-delete helper. Its csproj
    /// states the intent — "the only thing this project needs: a real AMQP client" — but a stated
    /// intent is not an enforced one, and every service's IntegrationTests now references it, so a
    /// dependency added here reaches all of them at once. Pinning it to zero project references
    /// keeps it shared test plumbing rather than a place service code accumulates.
    /// </summary>
    [Fact]
    public void BuildingBlocksTesting_ShouldDeclareNoProjectReferences()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.WithRing(ProjectRing.BuildingBlocksTesting))
        {
            var references = project.ProjectReferenceNames();
            if (references.Count != 0)
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", references)}] (expected none)");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "BuildingBlocks.Testing is shared test plumbing consumed by every service's "
            + $"IntegrationTests: it depends on nothing. Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void OnlyInfrastructureAndHost_ShouldReferenceBuildingBlocksInfrastructure()
    {
        // Arrange
        var offendingRings = new[]
        {
            ProjectRing.Domain,
            ProjectRing.Application,
            ProjectRing.Contracts,
            ProjectRing.BuildingBlocksCore,
            ProjectRing.BuildingBlocksTesting,
        };

        // Act
        // Both kinds of reference, not just ProjectReference: since the GL-25 split every repo
        // but giftlist-buildingblocks itself consumes BuildingBlocks.Infrastructure as a package
        // (ARCHITECTURE.md "Packaging: local feed"), so a project-references-only read of this
        // rule would pass vacuously in four of the five .NET repos.
        var offenders = RepoDiscovery.AllProjects
            .Where(p => offendingRings.Contains(p.Ring))
            .Where(p => p.ProjectReferenceNames().Contains(BuildingBlocksInfrastructureName) ||
                        p.PackageReferenceIds().Contains(BuildingBlocksInfrastructureName))
            .Select(p => p.Name)
            .ToList();

        // Assert
        Assert.True(offenders.Count == 0,
            "Only Infrastructure and Host may reference BuildingBlocks.Infrastructure, but these " +
            $"do not belong to either ring and reference it anyway: {string.Join(", ", offenders)}.");
    }

    private static string? FindBuiltAssembly(ProjectFile project)
    {
        var binDirectory = Path.Combine(project.Directory, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(binDirectory, project.Name + ".dll", SearchOption.AllDirectories)
            .OrderByDescending(f => f.Contains("Debug", StringComparison.Ordinal))
            .FirstOrDefault();
    }
}
