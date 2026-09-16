using System.Security.Cryptography;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// CONVENTIONS.md "Target framework" [AT]: no .csproj sets its own TargetFramework, because a
/// single <c>Directory.Build.props</c> sets it for every project that inherits it. Before the
/// GL-25 split that was one file at the monorepo root and MSBuild's own directory walk did the
/// work — there was exactly one copy, so nothing could drift.
///
/// <b>The split ends that.</b> MSBuild does not walk out of a repo, so every .NET repo root now
/// carries its own copy, and "five identical files kept identical by hand" is precisely the drift
/// that section exists to prevent. This test is the half that makes the copies mechanical rather
/// than aspirational: <c>repo-root-files.sha256</c>, checked in at this repo's root, records the
/// SHA-256 of every root file propagated from the canonical copy in
/// <c>giftlist-buildingblocks</c> by <c>giftlist-devenv/scripts/sync-repo-roots.sh</c>. A
/// hand-edited copy fails the build here instead of rotting quietly. Exactly the shape
/// <c>ArchitectureTestSyncTests</c> already uses for the Architecture/ suite itself.
///
/// <b>Why a hash manifest and not only <c>TargetFrameworkTests</c>'s verbatim string.</b> That
/// test pins this repo's Directory.Build.props against the block CONVENTIONS.md documents, which
/// is a stronger check — for that one file. It does not generalise: CONVENTIONS.md "Enforced
/// mechanically" puts <c>.editorconfig</c> under the same rule when it is added, and an
/// .editorconfig is not something anybody will keep as a C# string literal. The manifest carries
/// whatever the sync script propagates, so adding a file to that set is a change to the script
/// and nothing else.
///
/// What this does NOT check, so green is not read as more than it is: that the canonical copy
/// itself is correct. Every repo can agree on a wrong file. TargetFrameworkTests is what pins the
/// content against the document; this pins the copies against each other.
/// </summary>
public class RepoRootFileSyncTests
{
    /// <summary>
    /// The manifest may grow (CONVENTIONS.md "Enforced mechanically" adds .editorconfig later),
    /// but it may never shrink to nothing: an emptied manifest passes every assertion below
    /// while enforcing nothing, which is the failure mode this floor exists to catch.
    /// </summary>
    private static readonly string[] MustBeListed = { "Directory.Build.props", "nuget.config", "Directory.Build.targets" };

    [Fact]
    public void RepoRootFiles_ShouldMatchTheCheckedInManifest()
    {
        // Arrange
        var repoRoot = RepoDiscovery.RepoRoot;
        var manifestPath = Path.Combine(repoRoot, "repo-root-files.sha256");
        var offenders = new List<string>();
        var listed = new List<string>();

        // Act
        if (!File.Exists(manifestPath))
        {
            offenders.Add(
                $"manifest not found at {manifestPath} — run giftlist-devenv/scripts/sync-repo-roots.sh");
        }
        else
        {
            foreach (var line in File.ReadAllLines(manifestPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length != 2)
                {
                    offenders.Add($"malformed manifest line: '{line}'");
                    continue;
                }

                var relativeFile = parts[0].Trim();
                var expectedHash = parts[1].Trim();
                listed.Add(relativeFile);

                var filePath = Path.Combine(repoRoot, relativeFile);
                if (!File.Exists(filePath))
                {
                    offenders.Add($"{relativeFile}: listed in the manifest but missing from this repo root");
                    continue;
                }

                var actualHash = Sha256Hex(File.ReadAllBytes(filePath));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(
                        $"{relativeFile}: differs from the canonical copy in giftlist-buildingblocks " +
                        "— run giftlist-devenv/scripts/sync-repo-roots.sh rather than editing this copy");
                }
            }
        }

        // Assert
        var missingFromManifest = MustBeListed.Except(listed, StringComparer.Ordinal).ToList();
        Assert.True(missingFromManifest.Count == 0,
            "repo-root-files.sha256 no longer lists every file that must be identical across the " +
            $"repos, so this rule is passing vacuously for: {string.Join(", ", missingFromManifest)}.");
        Assert.True(offenders.Count == 0,
            "This repo's root files have drifted from the canonical copies. " +
            $"{string.Join("; ", offenders)}.");
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
