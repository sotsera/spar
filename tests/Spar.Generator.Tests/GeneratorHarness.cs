using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator.Tests;

/// <summary>How a test app references a library: as a project reference (its compilation) or as a built assembly.</summary>
public enum LibraryKind
{
    Project,
    Metadata,
}

/// <summary>
/// Compiles C# in memory against Spar and the BCL, runs the analyzer or the generator, and reports what came out. An
/// app can reference libraries and list some of them in <c>SparContracts</c>, as its project would.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Spar.Generated")]);

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static CSharpCompilation Compile(string source, OutputKind kind = OutputKind.ConsoleApplication, IEnumerable<MetadataReference>? libraries = null) =>
        Compile("App", source, kind, libraries);

    /// <summary>A library named <paramref name="name"/>, referenced the way <paramref name="kind"/> says.</summary>
    public static MetadataReference Library(string name, string source, LibraryKind kind)
    {
        var library = Compile(name, source, OutputKind.DynamicallyLinkedLibrary, null);
        if (kind == LibraryKind.Project)
        {
            return library.ToMetadataReference();
        }

        using var image = new MemoryStream();
        var emitted = library.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>The analyzer's diagnostics as "ID: located text", in source order.</summary>
    public static async Task<IReadOnlyList<string>> AnalyzeAsync(string source, IEnumerable<MetadataReference>? libraries = null, string listed = "") =>
        [.. (await DiagnosticsAsync(source, libraries, listed)).Select(Describe)];

    /// <summary>The analyzer's diagnostics as "ID: message", in source order.</summary>
    public static async Task<IReadOnlyList<string>> MessagesAsync(string source, IEnumerable<MetadataReference>? libraries = null, string listed = "") =>
        [.. (await DiagnosticsAsync(source, libraries, listed)).Select(d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")];

    /// <summary>Runs the generator (in <paramref name="variant"/>, with the project's <c>SparAppId</c>) and compiles the app with its output.</summary>
    public static GeneratorRun Generate(string source, string variant = "Development", IEnumerable<MetadataReference>? libraries = null, string listed = "", string appId = "")
    {
        var compilation = Compile(source, libraries: libraries);
        var driver = CSharpGeneratorDriver.Create(
            [new SparGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            optionsProvider: new BuildOptions(variant, listed, appId));
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

    private static CSharpCompilation Compile(string name, string source, OutputKind kind, IEnumerable<MetadataReference>? libraries) =>
        CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, path: name == "App" ? "Program.cs" : $"{name}.cs")],
            References.Value.Concat(libraries ?? []),
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));

    private static async Task<IReadOnlyList<Diagnostic>> DiagnosticsAsync(string source, IEnumerable<MetadataReference>? libraries, string listed)
    {
        var options = new AnalyzerOptions([], new BuildOptions("Development", listed, ""));
        var diagnostics = await Compile(source, libraries: libraries)
            .WithAnalyzers([new ContractAnalyzer(), new PageSecurityAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ThenBy(d => d.GetMessage(), StringComparer.Ordinal)];
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

    /// <summary>The build properties the generator and analyzer read: the variant, the <c>SparContracts</c> list and the app id.</summary>
    private sealed class BuildOptions(string variant, string listed, string appId) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(variant, listed, appId);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options(string variant, string listed, string appId) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key switch
                {
                    "build_property.SparVariant" => variant,
                    "build_property.SparContractAssemblies" => listed,
                    "build_property.SparAppId" when appId.Length > 0 => appId,
                    _ => null,
                };
                return value is not null;
            }
        }
    }
}

internal sealed record GeneratorRun(IReadOnlyDictionary<string, string> Files, IReadOnlyList<string> Errors);
