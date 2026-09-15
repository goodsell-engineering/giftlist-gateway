namespace ArchitectureTests.Support;

/// <summary>
/// Locates this repo's root (the nearest ancestor of the running tests that contains a *.sln)
/// and enumerates every .csproj under it. Scoped to this one repo on purpose: after the
/// Phase 2.5 split, each service's directory *is* its repo (ARCHITECTURE.md "Repository layout"), so a discovery
/// walk that never reaches outside it keeps this suite runnable standalone without any change.
/// </summary>
internal static class RepoDiscovery
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);
    private static readonly Lazy<IReadOnlyList<ProjectFile>> ProjectsLazy = new(DiscoverProjects);

    public static string RepoRoot => RepoRootLazy.Value;

    public static IReadOnlyList<ProjectFile> AllProjects => ProjectsLazy.Value;

    public static IEnumerable<ProjectFile> WithRing(ProjectRing ring) =>
        AllProjects.Where(p => p.Ring == ring);

    /// <summary>
    /// Every name this repo could plausibly mean by "a *.Contracts type" — the union of locally
    /// owned Contracts projects (found as a .csproj, same as any other ring) and consumed
    /// Contracts packages (found as a &lt;PackageReference&gt; ending ".Contracts" on some other
    /// project in this repo, e.g. Reservations.Infrastructure referencing GiftLists.Contracts).
    ///
    /// The package half exists because a consuming repo never has a local .csproj for another
    /// service's Contracts — not after the Phase 2.5 split, and not today either, since each
    /// repo's RepoDiscovery is already scoped to its own repo root. A rule that only looked for
    /// a Contracts .csproj would be permanently blind to exactly the case the ACL rule
    /// (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer" [AT]) exists to catch: Domain/Application quietly picking up a
    /// *consumed* contract type.
    /// </summary>
    public static IReadOnlyList<string> ContractsNames()
    {
        var owned = WithRing(ProjectRing.Contracts).Select(p => p.Name);
        var consumed = AllProjects
            .SelectMany(p => p.PackageReferenceIds())
            .Where(id => id.EndsWith(".Contracts", StringComparison.Ordinal));
        return owned.Concat(consumed).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Since the GL-25 split, Directory.Build.props lives at <see cref="RepoRoot"/> itself: one
    /// copy per .NET repo, propagated from giftlist-buildingblocks by
    /// giftlist-devenv/scripts/sync-repo-roots.sh and pinned against the others by
    /// <c>RepoRootFileSyncTests</c>. The upward walk is kept rather than simplified to a single
    /// Path.Combine because it costs nothing and stays correct if a clone is ever nested inside
    /// another checkout.
    /// </summary>
    public static string? FindDirectoryBuildProps()
    {
        var dir = new DirectoryInfo(RepoRoot);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find a repo root (a directory containing a *.sln) above '{AppContext.BaseDirectory}'.");
    }

    private static IReadOnlyList<ProjectFile> DiscoverProjects() =>
        Directory.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(ProjectFile.Load)
            .ToList();

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "bin" or "obj");
    }
}
