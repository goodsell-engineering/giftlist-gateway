using System.Xml.Linq;

namespace ArchitectureTests.Support;

/// <summary>
/// A discovered .csproj, classified into a <see cref="ProjectRing"/> and able to answer the
/// static, no-build-required questions: project references, package references, and whether it
/// declares its own TargetFramework. Closure questions that need a restore to answer live in
/// <see cref="ProjectAssets"/> instead.
/// </summary>
internal sealed record ProjectFile(string Name, string Path, ProjectRing Ring)
{
    public string Directory => System.IO.Path.GetDirectoryName(Path)
        ?? throw new InvalidOperationException($"'{Path}' has no parent directory.");

    public static ProjectFile Load(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return new ProjectFile(name, path, Classify(name));
    }

    /// <summary>
    /// <see cref="Load"/>, but for a path a caller only suspects is a project file — e.g. a
    /// <c>ProjectReference</c> <c>Include</c> resolved against another project's directory,
    /// which might point at a sibling service's Contracts project (GL-18: cross-repo, so
    /// invisible to <see cref="RepoDiscovery"/>'s own walk) or might just as easily be a typo'd
    /// or dangling relative path. Returns <see langword="null"/> rather than throwing so a caller
    /// can tell "resolves to a real project" apart from "doesn't" without a try/catch.
    /// </summary>
    public static ProjectFile? LoadIfExists(string path) =>
        File.Exists(path) ? Load(path) : null;

    public IReadOnlyList<string> ProjectReferenceNames() =>
        ProjectReferences().Select(r => r.Name).ToList();

    /// <summary>
    /// Every <c>ProjectReference</c>, as both the short name <see cref="ProjectReferenceNames"/>
    /// already exposed and the raw, unresolved <c>Include</c> path — the latter is what a caller
    /// needs to actually resolve the reference against <see cref="Directory"/> and load it (see
    /// <see cref="LoadIfExists"/>), which the name alone throws away.
    /// </summary>
    public IReadOnlyList<(string Name, string Include)> ProjectReferences() =>
        XDocument.Load(Path)
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => (
                Name: System.IO.Path.GetFileNameWithoutExtension(
                    v!.Replace('\\', System.IO.Path.DirectorySeparatorChar)),
                Include: v!))
            .ToList();

    public IReadOnlyList<string> PackageReferenceIds() =>
        XDocument.Load(Path)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

    public bool DeclaresTargetFrameworkExplicitly() =>
        XDocument.Load(Path)
            .Descendants()
            .Any(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks");

    private static ProjectRing Classify(string name) => name switch
    {
        "BuildingBlocks" => ProjectRing.BuildingBlocksCore,
        "BuildingBlocks.Infrastructure" => ProjectRing.BuildingBlocksInfrastructure,
        // GL-75: shared test-support helpers (today: the RabbitMQ queue-delete helper that used
        // to be duplicated four times). Exact-name match, like the two rows above — not a new
        // per-service ring.
        "BuildingBlocks.Testing" => ProjectRing.BuildingBlocksTesting,
        _ when name.EndsWith(".Domain", StringComparison.Ordinal) => ProjectRing.Domain,
        _ when name.EndsWith(".Application", StringComparison.Ordinal) => ProjectRing.Application,
        _ when name.EndsWith(".Infrastructure", StringComparison.Ordinal) => ProjectRing.Infrastructure,
        _ when name.EndsWith(".Host", StringComparison.Ordinal) => ProjectRing.Host,
        _ when name.EndsWith(".Contracts", StringComparison.Ordinal) => ProjectRing.Contracts,
        _ when name.EndsWith(".UnitTests", StringComparison.Ordinal) => ProjectRing.Test,
        _ when name.EndsWith(".IntegrationTests", StringComparison.Ordinal) => ProjectRing.Test,
        _ => ProjectRing.Unknown,
    };
}
