using System.Text.Json;

namespace ArchitectureTests.Support;

/// <summary>
/// Answers the questions that need a restore to have happened: the full transitive package
/// closure, and the restored target framework(s) — read from obj/project.assets.json rather than
/// emitted assembly references. That distinction is the whole point of this file: a
/// Cecil/NetArchTest-style check only sees types a project actually *uses*, so it stays green
/// right up until someone writes the violation. project.assets.json records everything
/// *reachable*, which is the guarantee CONVENTIONS.md "Project reference graph" actually asks for.
///
/// This deliberately does not shell out to `dotnet restore` itself. `dotnet build`/`dotnet test`
/// against the solution — the normal entry point, and the one this GL-10 report's verification
/// steps use — already restores every project in it, obj/project.assets.json included. Restoring
/// from inside a running test host is the kind of thing that looks convenient and turns into a
/// silent hang the first time it runs somewhere the outer process's environment isn't what a
/// top-level `dotnet` invocation would see.
/// </summary>
internal static class ProjectAssets
{
    public static IReadOnlyList<(string Name, string Type)> PackageClosure(ProjectFile project)
    {
        using var document = LoadAssets(project);
        var libraries = document.RootElement.GetProperty("libraries");
        var closure = new List<(string Name, string Type)>();
        foreach (var library in libraries.EnumerateObject())
        {
            var slash = library.Name.IndexOf('/');
            var name = slash >= 0 ? library.Name[..slash] : library.Name;
            var type = library.Value.TryGetProperty("type", out var typeProperty)
                ? typeProperty.GetString() ?? string.Empty
                : string.Empty;
            closure.Add((name, type));
        }

        return closure;
    }

    public static IReadOnlyList<string> TargetFrameworks(ProjectFile project)
    {
        using var document = LoadAssets(project);
        return document.RootElement
            .GetProperty("project")
            .GetProperty("frameworks")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToList();
    }

    private static string AssetsPath(ProjectFile project) =>
        Path.Combine(project.Directory, "obj", "project.assets.json");

    private static JsonDocument LoadAssets(ProjectFile project)
    {
        var path = AssetsPath(project);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{path} does not exist. Run 'dotnet restore' (or 'dotnet build'/'dotnet test') " +
                $"against the solution first — {project.Name} has not been restored, so its " +
                "package closure can't be checked.");
        }

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
