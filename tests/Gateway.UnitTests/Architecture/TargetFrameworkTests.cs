using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// CONVENTIONS.md "Target framework" [AT]: every assembly targets net10.0, no .csproj sets TargetFramework
/// itself, and the Directory.Build.props that centralises it is copied verbatim from the block
/// documented there. A mixed-TFM solution produces restore failures that read as unrelated
/// package problems, so this is checked on the restored result (project.assets.json), not just
/// on the .csproj text.
/// </summary>
public class TargetFrameworkTests
{
    private const string CanonicalDirectoryBuildProps =
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <LangVersion>latest</LangVersion>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        "  </PropertyGroup>\n" +
        "</Project>\n";

    [Fact]
    public void NoProject_ShouldDeclareItsOwnTargetFramework()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.AllProjects)
        {
            if (project.DeclaresTargetFrameworkExplicitly())
            {
                offenders.Add(project.Name);
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "These projects set <TargetFramework> themselves instead of inheriting it from " +
            $"Directory.Build.props: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void EveryProject_ShouldRestoreAgainstExactlyNet10()
    {
        // Arrange
        var offenders = new List<string>();

        // Act
        foreach (var project in RepoDiscovery.AllProjects)
        {
            var frameworks = ProjectAssets.TargetFrameworks(project);
            if (frameworks.Count != 1 || frameworks[0] != "net10.0")
            {
                offenders.Add($"{project.Name} -> [{string.Join(", ", frameworks)}]");
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"These projects did not restore against exactly net10.0: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void DirectoryBuildProps_ShouldMatchConventionsVerbatim()
    {
        // Arrange
        var path = RepoDiscovery.FindDirectoryBuildProps();

        // Act
        var actual = path is not null ? File.ReadAllText(path).Replace("\r\n", "\n") : null;

        // Assert
        Assert.NotNull(path);
        Assert.Equal(CanonicalDirectoryBuildProps, actual);
    }
}
