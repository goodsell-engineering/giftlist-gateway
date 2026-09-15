using System.Text.RegularExpressions;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// CONVENTIONS.md "Testing" [AT]: <c>Assert.Throws</c> and <c>.Should().Throw()</c> are banned across
/// both test projects — they fuse Act and Assert, which breaks the AAA structure every other
/// test in this codebase follows. Use <c>Record.Exception</c> as the Act and <c>Assert.IsType</c>
/// in the Assert instead.
///
/// This scans test source text rather than reflecting over compiled tests, so it needs nothing
/// built to run, and it deliberately skips this file's own folder — a rule that describes the
/// banned pattern in a diagnostic message would otherwise fail on itself.
/// </summary>
public class ExceptionAssertionRuleTests
{
    private static readonly Regex[] BannedPatterns =
    {
        new(@"Assert\s*\.\s*Throws"),
        new(@"\.Should\s*\(\s*\)\s*\.\s*Throw\b"),
    };

    [Fact]
    public void Tests_ShouldNeverUseAssertThrowsOrShouldThrow()
    {
        // Arrange
        // GL-75 review: BuildingBlocks.Testing classifies as its own ring, not Test (the Test ring
        // comes from the .UnitTests/.IntegrationTests name suffixes), so scanning Test alone would
        // exempt the one project a shared Assert.Throws helper would most naturally be written in.
        // GL-81: that judgement is no longer made here. SourceFiles owns the production/test split
        // for every rule, so this one cannot inherit a different answer than the next rule does.
        var testFiles = SourceFiles.AllTestCode().ToList();
        var offenders = new List<string>();

        // Act
        foreach (var (_, relativePath, text) in testFiles)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (normalized.Contains("/Architecture/", StringComparison.Ordinal))
            {
                continue;
            }

            if (BannedPatterns.Any(p => p.IsMatch(text)))
            {
                offenders.Add(normalized);
            }
        }

        // Assert
        Assert.True(offenders.Count == 0,
            "Assert.Throws/.Should().Throw() are banned (CONVENTIONS.md \"Testing\" [AT]) — use " +
            "Record.Exception as the Act and Assert.IsType in the Assert instead. Offending " +
            $"files: {string.Join(", ", offenders)}.");
    }
}
