#:package Microsoft.Build.Locator@1.11.2
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@5.9.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@5.9.0
#:property PublishAot=false
#:property ManagePackageVersionsCentrally=false
#:property DisableMSBuildAssemblyCopyCheck=true
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

// Loads a project the way both IDEs do (MSBuildWorkspace: a design-time build, then Roslyn), runs the project's own
// analyzers and generators, and edits a document in memory without saving or building.
// Usage: dotnet run ide-probe.cs -- Sample.Invalid/Sample.Invalid.csproj
MSBuildLocator.RegisterDefaults();
await Probe.RunAsync(Path.GetFullPath(args[0]));

internal static class Probe
{
    public static async Task RunAsync(string projectPath)
    {
        var clientStamp = Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(projectPath)!, "obj"), "client.stamp", SearchOption.AllDirectories)
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();

        var stopwatch = Stopwatch.StartNew();
        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(projectPath);
        Console.WriteLine($"opened {project.Name} in {stopwatch.ElapsedMilliseconds} ms; analyzers: {string.Join(", ", project.AnalyzerReferences.Select(a => a.Display))}");
        var stampAfter = Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(projectPath)!, "obj"), "client.stamp", SearchOption.AllDirectories)
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        Console.WriteLine($"client step ran during the design-time load: {stampAfter != clientStamp}");

        await ReportAsync(project, "as loaded");

        var document = project.Documents.Single(d => d.Name == "Invalid.cs");
        var text = (await document.GetTextAsync()).ToString()
            .Replace("Failed(string Message)", "Failed(string Detail)", StringComparison.Ordinal)
            .Replace("    Task<int> FineAsync(", "    Task<int> BrokenAsync(ref int value);\n\n    Task<int> FineAsync(", StringComparison.Ordinal);
        project = document.WithText(SourceText.From(text)).Project;
        await ReportAsync(project, "after an unsaved edit (Message renamed, BrokenAsync added)");
    }

    private static async Task ReportAsync(Project project, string label)
    {
        var stopwatch = Stopwatch.StartNew();
        var compilation = (await project.GetCompilationAsync())!;
        var analyzers = project.AnalyzerReferences.SelectMany(r => r.GetAnalyzers(project.Language)).ToImmutableArray();
        var diagnostics = await compilation.WithAnalyzers(analyzers, project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        var generated = await project.GetSourceGeneratedDocumentsAsync();
        Console.WriteLine($"[{label}] {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"  generated: {string.Join(", ", generated.Select(d => d.Name).Order(StringComparer.Ordinal))}");
        Console.WriteLine($"  compiler errors: {string.Join(", ", compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).Distinct())}");
        foreach (var diagnostic in diagnostics.Where(d => d.Id.StartsWith("SPAR", StringComparison.Ordinal) || d.Id.StartsWith("AD", StringComparison.Ordinal))
            .OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line))
        {
            var span = diagnostic.Location.GetLineSpan();
            Console.WriteLine($"  {diagnostic.Id} at {Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}) "
                + $"in source: {diagnostic.Location.IsInSource}");
        }
    }
}
