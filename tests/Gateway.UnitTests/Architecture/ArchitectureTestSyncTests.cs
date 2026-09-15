using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ArchitectureTests;

/// <summary>
/// GL-10's Architecture/ suite is deliberately duplicated verbatim across every service repo
/// (see DependencyRuleTests' header for why — a shared project would itself be a cross-repo
/// dependency the Phase 2.5 split has to unpick) rather than shared via a project reference.
///
/// That choice has a known failure mode: nothing detects one repo's copy silently drifting from
/// the canonical one once a fix lands there and isn't propagated everywhere else. It already
/// happened once — the CONVENTIONS.md "Project reference graph" Host row fix landed by hand in five repos in one batch, and nothing
/// would have caught a sixth copy being missed or one of the five being copied wrong.
///
/// <c>architecture-tests.sha256</c> (checked into this same repo, alongside this file, generated
/// by <c>scripts/sync-arch-tests.sh</c> records the expected SHA-256 of every file in the common set. If this repo's copy doesn't match, either this repo picked up a local
/// fix that hasn't gone through the canonical copy, or a canonical fix hasn't been synced here
/// yet — either way, the two have drifted, and this test is the tripwire for that.
/// </summary>
public class ArchitectureTestSyncTests
{
    [Fact]
    public void LocalArchitectureTestFiles_ShouldMatchTheCheckedInManifest()
    {
        // Arrange
        var architectureFolder = ArchitectureFolder();
        var manifestPath = Path.Combine(architectureFolder, "architecture-tests.sha256");
        var offenders = new List<string>();

        // Act
        if (!File.Exists(manifestPath))
        {
            offenders.Add($"manifest not found at {manifestPath} — run giftlist-devenv/scripts/sync-arch-tests.sh");
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
                var filePath = Path.Combine(architectureFolder, relativeFile);
                if (!File.Exists(filePath))
                {
                    offenders.Add($"{relativeFile}: listed in the manifest but missing on disk");
                    continue;
                }

                var actualHash = Sha256Hex(File.ReadAllBytes(filePath));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{relativeFile}: does not match the canonical copy — run giftlist-devenv/scripts/sync-arch-tests.sh");
                }
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            $"This repo's Architecture/ suite has drifted from the canonical copy. {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// The directory this very file lives in, captured at compile time via
    /// <see cref="CallerFilePathAttribute"/> rather than derived from the running assembly's
    /// output location — robust to build configuration, target framework moniker, and (after
    /// the Phase 2.5 split) which repo this is, none of which this file needs to know about.
    /// </summary>
    private static string ArchitectureFolder([CallerFilePath] string sourceFilePath = "") =>
        Path.GetDirectoryName(sourceFilePath)
        ?? throw new InvalidOperationException($"'{sourceFilePath}' has no parent directory.");

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
