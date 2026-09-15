using System.Text;
using System.Text.RegularExpressions;
using ArchitectureTests.Support;

namespace ArchitectureTests;

/// <summary>
/// GL-84 [AT]: <b>nothing</b> takes a <em>named</em> input port (<c>ICreateGiftList</c> and
/// friends) as a constructor parameter, field or local. Callers inject the open generic
/// <c>IInteractor&lt;TRequest, TResponse&gt;</c> instead — CONVENTIONS.md "Use cases", and every
/// service's own <c>IInteractor.cs</c> doc comment.
///
/// No carve-out, deliberately. An earlier draft of this rule exempted composition roots, on the
/// reasoning that they may legitimately name a port type. They may — but they name it as a
/// generic type argument (<c>AddScoped&lt;ICreateGiftList, ...&gt;</c>), which this detector does
/// not match anyway, so the exemption licensed exactly what CONVENTIONS.md "Use cases" forbids
/// absolutely while exempting nothing that exists. A test weaker than the convention it asserts
/// is how ARCHITECTURE.md's own prose drifted in the first place.
///
/// This exists because the claim had been living in prose alone and had gone false without
/// anything noticing. ARCHITECTURE.md said adapters "depend on the interface, never the class"
/// and that an interactor is "reachable only through its input port"; no code has done that since
/// GL-20, and the sentences still read as true on a skim because <c>IInteractor&lt;,&gt;</c> is
/// arguably "an input port" too. The one doc/code checker this project has walks CONVENTIONS.md
/// against the conventions skill and resolves citations — ARCHITECTURE.md has no canonical pair,
/// so it structurally could not see the file that contradicted the convention.
///
/// The failure this prevents is concrete and was reached once already, in GL-65: a named port in
/// a constructor compiles, passes every unit test, and throws <c>Unable to resolve service</c> on
/// the first message off RabbitMQ, because nothing registers the named ports. Worse, it would
/// silently escape the <c>Validating&lt;,&gt;</c>/<c>Logging&lt;,&gt;</c> decorator pair, which is
/// keyed on the open generic.
///
/// Direction matters: this catches the <em>code</em> moving under the doc, which is the direction
/// this actually drifted. It says nothing about the prose, and it survives the GL-25 split
/// unchanged because it reads only this repo.
/// </summary>
public class InputPortInjectionRuleTests
{
    /// <summary>
    /// A named input port declared as the type of something — a constructor parameter, a field,
    /// a local. Anchored on "type name, whitespace, lower-camel identifier", which is what a
    /// declaration looks like and what <c>: ICreateGiftList</c> (implements),
    /// <c>AddScoped&lt;ICreateGiftList, ...&gt;</c> (registers) and <c>nameof(ICreateGiftList)</c>
    /// all deliberately do not.
    /// </summary>
    private static Regex DeclarationOf(string portName) =>
        new($@"\b{Regex.Escape(portName)}\s+[a-z_]\w*");

    private static readonly Regex PortDeclaration =
        new(@"\binterface\s+I(?<useCase>[A-Z]\w*)\b");

    private static readonly Regex InteractorDeclaration =
        new(@"\b(?:class|record)\s+(?<name>[A-Z]\w*Interactor)\b");

    [Fact]
    public void NamedInputPorts_ShouldNeverBeInjected()
    {
        // Arrange
        var allFiles = SourceFiles.AllProductionCode().ToList();
        var ports = NamedInputPorts(allFiles);
        var offenders = new List<string>();

        // Act
        foreach (var (_, relativePath, text) in allFiles)
        {
            var code = StripCommentsAndStrings(text);
            foreach (var port in ports)
            {
                if (DeclarationOf(port).IsMatch(code))
                {
                    offenders.Add($"{relativePath} declares a {port}");
                }
            }
        }

        // Assert
        // Population first. A repo with no interactors (BuildingBlocks, and Reservations until
        // Phase 4) legitimately has no named ports at all, so an unconditional Count > 0 would go
        // red for a correct reason — condition it on there being an interactor to pair with.
        Assert.NotEmpty(allFiles);
        var hasAnyInteractor = allFiles.Any(f => InteractorDeclaration.IsMatch(f.Text));
        Assert.True(!hasAnyInteractor || ports.Count > 0,
            "This repo declares at least one *Interactor but no named input port was discovered, " +
            "so this rule scanned for nothing and passed vacuously. Either the port-discovery " +
            "regex has stopped matching, or a use case has an interactor with no paired port " +
            "(which NamingConventionTests.InputPorts_ShouldHaveAMatchingInteractorAndRequest " +
            "covers from the other side).");

        Assert.True(offenders.Count == 0,
            "A named input port is being injected (GL-84 [AT]). Callers inject the open generic " +
            "IInteractor<TRequest, TResponse>; the named ports exist only to pair a use case with " +
            "its interactor and request for the naming test, and nothing registers them — so this " +
            "compiles, passes unit tests, and throws 'Unable to resolve service' at runtime. " +
            $"Offenders: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void TheNamedPortInjectionProbe_ShouldJudgeKnownShapesCorrectly()
    {
        // Arrange
        // Both halves of the detector are pinned here, because either one silently reversed
        // leaves the rule above green: a declaration regex that matches nothing, or one that
        // matches the two shapes that are supposed to be legal.
        var port = "ICreateGiftList";
        var samples = new[]
        {
            (Shape: "constructor injection",
             Code: "internal sealed class GiftListsGrpcService\n{\n" +
                   "    public GiftListsGrpcService(ICreateGiftList createGiftList) { }\n}\n",
             Expected: true),
            (Shape: "readonly field",
             Code: "internal sealed class Handler\n{\n" +
                   "    private readonly ICreateGiftList _createGiftList;\n}\n",
             Expected: true),
            (Shape: "primary constructor",
             Code: "internal sealed class Handler(ICreateGiftList createGiftList);\n",
             Expected: true),
            (Shape: "implementing the port",
             Code: "internal sealed class CreateGiftListInteractor : ICreateGiftList\n{\n}\n",
             Expected: false),
            (Shape: "registering the port",
             Code: "services.AddScoped<ICreateGiftList, CreateGiftListInteractor>();\n",
             Expected: false),
            (Shape: "naming it in a comment",
             Code: "// the named ports (ICreateGiftList and friends) are never injected\n",
             Expected: false),
            (Shape: "naming it in a string",
             Code: "var message = \"ICreateGiftList createGiftList would not resolve\";\n",
             Expected: false),
            // A raw string holding an ODD number of quotes: a scanner that does not understand
            // the """ fence desynchronises here and swallows everything after it, including the
            // injection on the next line. Chosen deliberately over a balanced sample, which a
            // naive scanner gets right by luck and which therefore proves nothing.
            (Shape: "a raw string with a lone quote, followed by a real injection",
             Code: "var sample = \"\"\"a \" quote\"\"\";\n" +
                   "internal sealed class Handler(ICreateGiftList createGiftList);\n",
             Expected: true),
        };

        // Act
        var judgements = samples
            .Select(s => (s.Shape, Flagged: DeclarationOf(port).IsMatch(StripCommentsAndStrings(s.Code)), s.Expected))
            .ToList();

        // Assert
        foreach (var (shape, flagged, expected) in judgements)
        {
            Assert.True(flagged == expected,
                $"The detector judged the '{shape}' sample as flagged={flagged}, expected " +
                $"{expected}. NamedInputPorts_ShouldNeverBeInjected is " +
                "only as good as this.");
        }
    }

    /// <summary>
    /// The named input ports this repo declares: an <c>I{UseCase}</c> interface in the Application
    /// ring outside <c>Common/</c>. Mirrors
    /// <c>NamingConventionTests.InputPorts_ShouldHaveAMatchingInteractorAndRequest</c>'s discovery
    /// exactly, including why <c>I*Repository</c> and the domain-agnostic <c>Common/</c> ports
    /// (<c>IClock</c>, <c>IValidator</c>) are excluded — those are collaborators services really
    /// do inject.
    /// </summary>
    private static IReadOnlyList<string> NamedInputPorts(
        IEnumerable<(string Path, string RelativePath, string Text)> allFiles)
    {
        var ports = new List<string>();
        foreach (var (path, _, text) in allFiles)
        {
            var inApplication = RepoDiscovery.WithRing(ProjectRing.Application)
                .Any(p => path.StartsWith(p.Directory, StringComparison.Ordinal));
            if (!inApplication || path.Replace('\\', '/').Contains("/Common/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in PortDeclaration.Matches(text))
            {
                var useCase = match.Groups["useCase"].Value;
                if (useCase.EndsWith("Repository", StringComparison.Ordinal))
                {
                    continue;
                }

                ports.Add($"I{useCase}");
            }
        }

        return ports.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Blanks out comments, string literals and char literals so the scan sees code only. Without
    /// this the rule fires on every doc comment that explains why the named ports are not
    /// injected — which is most of them, this project being comment-heavy on purpose.
    /// </summary>
    private static string StripCommentsAndStrings(string text)
    {
        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    output.Append(text[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                i = Math.Min(i + 2, text.Length);
                continue;
            }

            // Raw string literal: """ (or more). No production code uses one today, but
            // ProjectionWriteRuleTests already does, and the naive scanner below would read the
            // three quotes as open/close/open and then swallow the rest of the file.
            if (c == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                var fence = 0;
                while (i < text.Length && text[i] == '"')
                {
                    fence++;
                    i++;
                }

                var run = 0;
                while (i < text.Length && run < fence)
                {
                    run = text[i] == '"' ? run + 1 : 0;
                    i++;
                }

                continue;
            }

            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i += 2;
                while (i < text.Length)
                {
                    if (text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    if (text[i] == '"')
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    i += text[i] == '\\' ? 2 : 1;
                }

                i++;
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }
}
