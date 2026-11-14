#:package Microsoft.Build.Locator@1.11.2
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false
#:property TreatWarningsAsErrors=false
#:property DisableMSBuildAssemblyCopyCheck=true
using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

MSBuildLocator.RegisterDefaults();
await Probe.RunAsync(args[0]);

static class Probe
{
    public static async Task RunAsync(string projectPath)
    {
        var stamp = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "Debug", "net11.0", "win-x64", "spar", "client.stamp");
        var stampBefore = File.Exists(stamp) ? File.GetLastWriteTimeUtc(stamp) : default;
        var sw = Stopwatch.StartNew();
        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e => Console.WriteLine($"  workspace {e.Diagnostic.Kind}: {e.Diagnostic.Message}"));
        var project = await workspace.OpenProjectAsync(projectPath);
        Console.WriteLine($"opened {project.Name} in {sw.ElapsedMilliseconds} ms; analyzers: {string.Join(", ", project.AnalyzerReferences.Select(a => a.Display))}");
        Console.WriteLine($"client.stamp touched by design-time load: {(File.Exists(stamp) ? File.GetLastWriteTimeUtc(stamp) : default) != stampBefore}");

        await Report(project, "as loaded");

        // An IDE edit: add a command, with no build in between.
        var doc = project.Documents.Single(d => d.Name == "NotesService.cs");
        var text = (await doc.GetTextAsync()).ToString().Replace(
            "    [SparCommand]\n    public ValueTask<int> CountAsync()",
            "    [SparCommand]\n    public Task<string> PingAsync(string message) => Task.FromResult(message);\n\n    [SparCommand]\n    public ValueTask<int> CountAsync()");
        project = doc.WithText(SourceText.From(text)).Project;
        await Report(project, "after adding PingAsync in memory");

        // An unsupported wire type, still in memory.
        text = text.Replace("PingAsync(string message)", "PingAsync(Dictionary<string, int> message)").Replace("Task.FromResult(message)", "Task.FromResult(\"x\")");
        project = doc.WithText(SourceText.From(text)).Project;
        await Report(project, "after unsupported type in memory");
    }

    private static async Task Report(Project project, string label)
    {
        var generated = await project.GetSourceGeneratedDocumentsAsync();
        var compilation = (await project.GetCompilationAsync())!;
        var binding = generated.FirstOrDefault(d => d.Name == "NotesServiceBinding.g.cs");
        var bindingText = binding is null ? "" : (await binding.GetTextAsync()).ToString();
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).Distinct();

        // Generator-reported diagnostics, as the IDE surfaces them.
        var generators = project.AnalyzerReferences.SelectMany(r => r.GetGenerators(LanguageNames.CSharp)).ToArray();
        var authored = compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(t => t.FilePath.Contains("Spike.Generator")));
        CSharpGeneratorDriver.Create(generators, parseOptions: (CSharpParseOptions)project.ParseOptions!).RunGeneratorsAndUpdateCompilation(authored, out _, out var generatorDiagnostics);

        Console.WriteLine($"[{label}]");
        Console.WriteLine($"  generated: {string.Join(", ", generated.Select(d => d.Name))}");
        Console.WriteLine($"  SparCatalog type: {compilation.GetTypeByMetadataName("Spar.Generated.SparCatalog") is not null}; binding has ping: {bindingText.Contains("\"ping\"")}");
        Console.WriteLine($"  compiler errors: {string.Join(", ", errors)}");
        foreach (var d in generatorDiagnostics)
        {
            Console.WriteLine($"  generator: {d.Id} at {d.Location.GetLineSpan().Path}({d.Location.GetLineSpan().StartLinePosition.Line + 1},{d.Location.GetLineSpan().StartLinePosition.Character + 1}): {d.GetMessage()}");
        }
    }
}
