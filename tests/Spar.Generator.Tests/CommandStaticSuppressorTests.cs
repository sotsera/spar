using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeQuality.Analyzers.QualityGuidelines;

namespace Spar.Generator.Tests;

/// <summary>
/// CA1822 ("can be marked as static") is hidden on a contract class's commands, where a static method would stop being
/// a command, and kept everywhere else. The rule is the real one: the test project references the .NET analyzers of the
/// SDK that builds it.
/// </summary>
public sealed class CommandStaticSuppressorTests
{
    [Fact]
    public async Task Hides_CA1822_on_commands_of_a_contract_class_only()
    {
        const string source = """
            using System.Threading.Tasks;
            using Spar;

            [SparContract]
            public sealed class GreetingService
            {
                [SparCommand]
                public Task<string> GreetAsync(string name) => Task.FromResult(name);

                public string Format(string name) => "Hello, " + name;
            }

            public sealed class NotAContract
            {
                [SparCommand]
                public Task<string> GreetAsync(string name) => Task.FromResult(name);
            }
            """;

        var states = await CA1822StatesAsync(source);

        Assert.Equal(
            [
                "GreetAsync: suppressed",
                "Format: reported",
                "GreetAsync: reported",
            ],
            states);
    }

    private static async Task<IReadOnlyList<string>> CA1822StatesAsync(string source)
    {
        var compilation = GeneratorHarness.Compile(source, OutputKind.DynamicallyLinkedLibrary);
        var options = compilation.Options.WithSpecificDiagnosticOptions(
            new Dictionary<string, ReportDiagnostic> { ["CA1822"] = ReportDiagnostic.Warn });
        var analyzers = NetAnalyzersFor("CA1822").Add(new CommandStaticSuppressor());
        var withAnalyzers = compilation.WithOptions(options).WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([]),
                onAnalyzerException: null,
                concurrentAnalysis: false,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: true));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        return [.. diagnostics
            .Where(d => d.Id == "CA1822")
            .OrderBy(d => d.Location.SourceSpan.Start)
            .Select(d => $"{d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)}: {(d.IsSuppressed ? "suppressed" : "reported")}")];
    }

    /// <summary>The SDK's CA1822 analyzer, referenced by the test project.</summary>
    private static ImmutableArray<DiagnosticAnalyzer> NetAnalyzersFor(string id)
    {
        DiagnosticAnalyzer analyzer = new MarkMembersAsStaticAnalyzer();
        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == id);
        return [analyzer];
    }
}
