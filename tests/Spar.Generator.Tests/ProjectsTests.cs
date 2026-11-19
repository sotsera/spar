using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Spar.Generator.Tests;

/// <summary>
/// The <c>Projects</c> class of a black-box test project (decision "M1b's remaining questions without a spike"): one
/// <c>SparProject</c> per tested app, from the <c>AdditionalFiles</c> items Spar.Sdk passes with their metadata, only
/// when the project sets <c>SparBlackBoxTests</c>; names that are no C# identifiers still make one.
/// </summary>
public sealed class ProjectsTests
{
    // The test project's side: Spar.Testing's SparProject, as a stand-in with its constructor.
    private const string TestSource = "namespace Spar.Testing { public sealed class SparProject(string n, string i, string e, string d) { } }";

    [Fact]
    public void Each_tested_app_becomes_a_property_of_Projects()
    {
        var run = Generate(true,
            ("Notebook.csproj", App("Notebook", "com.example.Notebook", "Notebook.exe", @"C:\src\Notebook\")),
            ("My.Tool.csproj", App("My.Tool", "com.example.MyTool", "My.Tool.exe", @"C:\src\My Tool\")),
            ("2Do.csproj", App("2Do", "com.example.ToDo", "2Do.exe", "")),
            ("notes.txt", []));
        Assert.True(run.Errors.Count == 0, string.Join(Environment.NewLine, run.Errors));

        var code = run.Files["Spar.Projects.g.cs"];
        Assert.Contains("internal static class Projects", code, StringComparison.Ordinal);
        Assert.Contains("public static global::Spar.Testing.SparProject Notebook { get; } = new(", code, StringComparison.Ordinal);
        Assert.Contains("public static global::Spar.Testing.SparProject My_Tool { get; } = new(", code, StringComparison.Ordinal);
        Assert.Contains("public static global::Spar.Testing.SparProject _2Do { get; } = new(", code, StringComparison.Ordinal);
        Assert.Contains("\"com.example.MyTool\",", code, StringComparison.Ordinal);
        Assert.Contains(@"""C:\\src\\My Tool\\"");", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_SparBlackBoxTests_there_is_no_Projects_class()
    {
        var run = Generate(false, ("Notebook.csproj", App("Notebook", "com.example.Notebook", "Notebook.exe", "")));

        Assert.Empty(run.Errors);
        Assert.DoesNotContain("Spar.Projects.g.cs", run.Files.Keys);
    }

    private static Dictionary<string, string> App(string name, string appId, string executable, string directory) => new()
    {
        ["build_metadata.AdditionalFiles.SparBlackBoxApp"] = "true",
        ["build_metadata.AdditionalFiles.SparAppName"] = name,
        ["build_metadata.AdditionalFiles.SparAppId"] = appId,
        ["build_metadata.AdditionalFiles.SparAppExecutable"] = executable,
        ["build_metadata.AdditionalFiles.SparAppDirectory"] = directory,
    };

    private static GeneratorRun Generate(bool blackBox, params (string Path, Dictionary<string, string> Metadata)[] files)
    {
        var compilation = GeneratorHarness.Compile(TestSource, OutputKind.DynamicallyLinkedLibrary);
        var parse = (CSharpParseOptions)compilation.SyntaxTrees[0].Options;
        var texts = files.Select(file => (AdditionalText)new Text(file.Path)).ToImmutableArray();
        var options = new FileOptions(blackBox, files.ToDictionary(file => file.Path, file => file.Metadata, StringComparer.Ordinal));
        var driver = CSharpGeneratorDriver.Create([new SparGenerator().AsSourceGenerator()], texts, parse, options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, TestContext.Current.CancellationToken);

        var generated = output.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken).Concat(diagnostics)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        return new GeneratorRun(generated, errors);
    }

    private sealed class Text(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken cancellationToken = default) => SourceText.From("");
    }

    private sealed class FileOptions(bool blackBox, Dictionary<string, Dictionary<string, string>> files) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } =
            new Options(blackBox ? new() { ["build_property.SparBlackBoxTests"] = "true" } : []);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(files[textFile.Path]);

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
        }
    }
}
