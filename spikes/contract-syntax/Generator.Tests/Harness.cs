using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Spar.Generator;

namespace Generator.Tests;

/// <summary>Compiles sources in memory, runs the generator and the analyzer, and loads the result when asked.</summary>
internal static class Harness
{
    // Roslyn 5.9 treats C# 15's closed classes as a preview feature; the SDK's Roslyn 5.12 does not.
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    private static readonly ImmutableArray<MetadataReference> Framework =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Spar.Generator", StringComparison.Ordinal))
            .Select(p => MetadataReference.CreateFromFile(p)),
    ];

    private const string Usings = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Spar;\n";

    public static CSharpCompilation Compile(string source, string assemblyName = "App", params MetadataReference[] references) =>
        CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(Usings + source, ParseOptions, path: assemblyName + ".cs")],
            [.. Framework, .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>A referenced library: from metadata (as the command-line build sees it) or as a project reference (as the IDE sees it).</summary>
    public static MetadataReference Library(string source, string name, bool metadata = true)
    {
        var compilation = Compile(source, name);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        if (!metadata)
        {
            return compilation.ToMetadataReference();
        }

        using var image = new MemoryStream();
        Assert.True(compilation.Emit(image).Success);
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    public static RunResult Run(string source, params MetadataReference[] references) => Run(Compile(source, "App", references));

    public static RunResult Run(CSharpCompilation compilation)
    {
        // The sources may use generated code (Spar.Generated), so they are checked after generation, in the output.
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ContractGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var sources = driver.GetRunResult().GeneratedTrees.ToDictionary(t => Path.GetFileName(t.FilePath), t => t.GetText().ToString());

        var analyzerDiagnostics = compilation
            .WithAnalyzers([new ContractAnalyzer()], new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: false, logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false))
            .GetAllDiagnosticsAsync().GetAwaiter().GetResult()
            .Where(d => d.Id.StartsWith("SPAR", StringComparison.Ordinal) || d.Id.StartsWith("AD", StringComparison.Ordinal))
            .ToImmutableArray();

        // Errors anywhere, and warnings in generated code (the test sources may have their own warnings).
        var outputErrors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree is { } tree && !compilation.ContainsSyntaxTree(tree)))
            .ToImmutableArray();
        var set = ContractSet.Analyze(ContractDiscovery.ReadLocal(compilation, default).Concat(ContractDiscovery.ReadReferenced(compilation, default)));
        return new RunResult(compilation, output, analyzerDiagnostics, generatorDiagnostics, outputErrors, sources, set, ClientEmitter.EmitPackages(set.Contracts));
    }

    /// <summary>Emits the generated compilation and loads it in a collectible context (tests only).</summary>
    public static Assembly Load(Compilation output, out AssemblyLoadContext context)
    {
        using var image = new MemoryStream();
        var emit = output.Emit(image);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        context = new AssemblyLoadContext(output.AssemblyName, isCollectible: true);
        image.Position = 0;
        return context.LoadFromStream(image);
    }

    public static string SnapshotDirectory([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "Snapshots");
}

internal sealed record RunResult(
    CSharpCompilation Input,
    Compilation Output,
    ImmutableArray<Diagnostic> Analyzer,
    ImmutableArray<Diagnostic> Generator,
    ImmutableArray<Diagnostic> OutputProblems,
    Dictionary<string, string> Sources,
    ContractSet Set,
    SortedDictionary<string, string> Client)
{
    /// <summary>Every SPAR diagnostic as "ID 'located text': message", or "ID (no location): message".</summary>
    public List<string> Diagnostics =>
    [
        .. Analyzer.Concat(Generator)
            .Select(d => d.Location.IsInSource
                ? $"{d.Id} '{d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)}': {d.GetMessage()}"
                : $"{d.Id} (no location): {d.GetMessage()}")
            .Order(StringComparer.Ordinal),
    ];

    public string? Binding(string contract) => Sources.GetValueOrDefault($"SparBinding.{contract}.g.cs");

    public void AssertClean()
    {
        Assert.Empty(Diagnostics);
        AssertGeneratedCompiles();
    }

    public void AssertGeneratedCompiles() => Assert.True(OutputProblems.IsEmpty, string.Join("\n", OutputProblems));

    /// <summary>Asserts exactly these diagnostics: (id, located text) pairs; null text means "no location".</summary>
    public void AssertDiagnostics(params (string Id, string? Located)[] expected)
    {
        var actual = Analyzer.Concat(Generator)
            .Select(d => (d.Id, Located: d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null))
            .Order()
            .ToList();
        Assert.True(expected.Order().SequenceEqual(actual),
            $"Expected:\n  {string.Join("\n  ", expected.Order())}\nActual:\n  {string.Join("\n  ", Diagnostics)}");
        AssertGeneratedCompiles();
    }
}
