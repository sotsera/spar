using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator.Tests;

/// <summary>Compiles C# in memory against Spar and the BCL, runs the analyzer or the generator, and reports what came out.</summary>
internal static class GeneratorHarness
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Spar.Generated")]);

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static CSharpCompilation Compile(string source, OutputKind kind = OutputKind.ConsoleApplication) =>
        CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Program.cs")],
            References.Value,
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>The analyzer's diagnostics as "ID: located text", in source order.</summary>
    public static async Task<IReadOnlyList<string>> AnalyzeAsync(string source)
    {
        var compilation = Compile(source);
        var diagnostics = await compilation
            .WithAnalyzers([new ContractAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start).Select(Describe)];
    }

    /// <summary>Runs the generator (in <paramref name="variant"/>) and compiles the app with its output.</summary>
    public static GeneratorRun Generate(string source, string variant = "Development")
    {
        var compilation = Compile(source);
        var driver = CSharpGeneratorDriver.Create(
            [new SparGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            optionsProvider: new VariantOptions(variant));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        var files = output.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Concat(generatorDiagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToList();
        return new GeneratorRun(files, errors);
    }

    private static string Describe(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.SourceSpan;
        var text = diagnostic.Location.SourceTree?.GetText().ToString(span) ?? "";
        return $"{diagnostic.Id}: {text}";
    }

    /// <summary>The BCL and this test's own dependencies (Spar, DI): the runtime lists them all as trusted assemblies.</summary>
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return [.. trusted.Where(path => !Path.GetFileName(path).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path))];
    }

    private sealed class VariantOptions(string variant) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(variant);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options(string variant) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key == "build_property.SparVariant" ? variant : null;
                return value is not null;
            }
        }
    }
}

internal sealed record GeneratorRun(IReadOnlyDictionary<string, string> Files, IReadOnlyList<string> Errors);
