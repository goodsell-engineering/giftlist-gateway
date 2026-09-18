using System.Text;
using System.Text.RegularExpressions;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// GL-38: ARCHITECTURE.md "Defence in depth on the owner-facing path" — "the reservation
/// projection port is injected <em>only</em> into that one interactor [<c>ViewGiftList</c>], and
/// the architecture tests assert no other type in the Gateway references it." This is that
/// assertion. Gateway-only, and deliberately not part of the synced common set: the Gateway is
/// the one service that serves both an owner and a guest a view of the same list, so it is the
/// one place the rule has something to bind to (same precedent as
/// <c>ProjectionWriteBindingTests</c>).
///
/// <para><b>What "references" means here.</b> The port's name appearing in code — comments and
/// strings stripped, since half the files in this repo explain the rule in prose. The four files
/// allowed to name it are the port itself, the interactor the rule is about, the Infrastructure
/// class that implements it, and the composition root that registers it. Anything else naming
/// <c>IReservationProjectionRepository</c> is a second reader of reservation data, which is the
/// thing the section rules out — including, deliberately, the pass-through interactor behind
/// <c>GiftReservedV1</c>, which is why the write side is a separate port
/// (<c>Gateway.Application.Common.IReservationProjectionWriter</c>) rather than a second method
/// on this one.</para>
///
/// <para><b>Non-vacuity.</b> The allowlist is also asserted positively: the interactor and the
/// implementation must actually name the port. A rename of either that this test was not told
/// about would otherwise leave it green with nothing bound.</para>
/// </summary>
public class ReservationProjectionPortIsolationTests
{
    private const string PortName = "IReservationProjectionRepository";

    private const string InteractorPath = "src/Gateway.Application/GiftLists/ViewGiftList/ViewGiftListInteractor.cs";

    private const string ImplementationPath = "src/Gateway.Infrastructure/Reservations/Persistence/ReservationProjectionRepository.cs";

    private static readonly string[] AllowedPaths =
    [
        "src/Gateway.Application/Reservations/IReservationProjectionRepository.cs",
        InteractorPath,
        ImplementationPath,
        "src/Gateway.Infrastructure/Platform/GatewayInfrastructureServiceCollectionExtensions.cs",
    ];

    private static readonly Regex PortReference = new($@"\b{Regex.Escape(PortName)}\b", RegexOptions.Compiled);

    [Fact]
    public void TheReservationProjectionPort_ShouldBeReferencedOnlyByViewGiftListAndItsWiring()
    {
        // Arrange
        var offenders = new List<string>();
        var referencingFiles = new List<string>();

        // Act
        foreach (var (_, relativePath, text) in SourceFiles.AllProductionCode())
        {
            var normalised = relativePath.Replace('\\', '/');
            if (!PortReference.IsMatch(StripCommentsAndStrings(text)))
            {
                continue;
            }

            referencingFiles.Add(normalised);
            if (!AllowedPaths.Contains(normalised, StringComparer.Ordinal))
            {
                offenders.Add(normalised);
            }
        }

        // Assert
        Assert.Contains(InteractorPath, referencingFiles);
        Assert.Contains(ImplementationPath, referencingFiles);
        Assert.True(offenders.Count == 0,
            $"ARCHITECTURE.md \"Defence in depth on the owner-facing path\": {PortName} may be held by " +
            "ViewGiftListInteractor and nothing else — a second reader of reservation data is a " +
            "second place the visibility rule has to be remembered. If the new reader is a " +
            $"writer, use IReservationProjectionWriter instead. Offenders: {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// Blanks out <c>//</c> and <c>/* */</c> comments and string/char literals so the scan sees
    /// code only. A local copy of the same routine <c>InputPortInjectionRuleTests</c> keeps
    /// private — that file is synced verbatim across every repo and cannot grow a public helper
    /// for a Gateway-only test without drifting from its manifest.
    /// </summary>
    private static string StripCommentsAndStrings(string text)
    {
        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else if (text[i] == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                var end = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 3;
            }
            else if (text[i] == '"' || text[i] == '\'')
            {
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    i += text[i] == '\\' ? 2 : 1;
                }

                i++;
            }
            else
            {
                output.Append(text[i]);
                i++;
            }
        }

        return output.ToString();
    }
}
