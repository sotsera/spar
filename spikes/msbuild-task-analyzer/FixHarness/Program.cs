// Loads the analyzer package's analyzers and code fix providers, reports which diagnostics have a fix, and applies
// every offered fix to its own copy of the original solution, so the effect of each fix is visible in isolation.
// Usage: FixHarness <analyzer.dll> <references.txt> <output-dir> <source.cs>...
using System.Collections.Immutable;
using System.Composition;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: FixHarness <analyzer.dll> <references.txt> <output-dir> <source.cs>...");
    return 2;
}

var analyzerPath = Path.GetFullPath(args[0]);
var references = File.ReadAllLines(args[1]).Where(l => l.Length > 0).Select(l => MetadataReference.CreateFromFile(l)).ToArray();
var outputDir = Path.GetFullPath(args[2]);
Directory.CreateDirectory(outputDir);
var sources = args.Skip(3).Select(Path.GetFullPath).ToArray();

var assembly = Assembly.LoadFrom(analyzerPath);
var types = assembly.GetTypes();
var analyzers = types
    .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
    .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
    .ToImmutableArray();
var suppressors = analyzers.OfType<DiagnosticSuppressor>().ToArray();
var fixers = types
    .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
    .Select(t => (CodeFixProvider)Activator.CreateInstance(t)!)
    .ToArray();

var report = new StringBuilder();
report.AppendLine($"# analyzer: {assembly.GetName().Name} {assembly.GetName().Version}, Roslyn host {typeof(Compilation).Assembly.GetName().Version}");
foreach (var analyzer in analyzers)
{
    var kind = analyzer is DiagnosticSuppressor ? "suppressor" : "analyzer";
    var ids = analyzer is DiagnosticSuppressor s
        ? string.Join(", ", s.SupportedSuppressions.Select(x => $"{x.Id}->{x.SuppressedDiagnosticId}"))
        : string.Join(", ", analyzer.SupportedDiagnostics.Select(d => $"{d.Id}({d.DefaultSeverity}{(d.IsEnabledByDefault ? "" : ",disabled")})"));
    report.AppendLine($"{kind} {analyzer.GetType().Name}: {ids}");
}

foreach (var fixer in fixers)
{
    var export = fixer.GetType().GetCustomAttribute<ExportCodeFixProviderAttribute>();
    var mef2 = fixer.GetType().GetCustomAttributes().Any(a => a.GetType().FullName == typeof(ExportAttribute).FullName);
    report.AppendLine($"fixer {fixer.GetType().Name}: ids [{string.Join(", ", fixer.FixableDiagnosticIds)}], " +
        $"[ExportCodeFixProvider]={(export is null ? "absent" : string.Join("/", export.Languages))}, " +
        $"[Shared]={fixer.GetType().GetCustomAttribute<SharedAttribute>() is not null}, mef2Export={mef2}, " +
        $"fixAll={fixer.GetFixAllProvider()?.GetType().Name ?? "none"}");
}

using var workspace = new AdhocWorkspace();
var parse = new CSharpParseOptions(LanguageVersion.Preview);
var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Probe", "Probe", LanguageNames.CSharp,
    compilationOptions: options, parseOptions: parse, metadataReferences: references);
var solution = workspace.CurrentSolution.AddProject(projectInfo);
var projectId = projectInfo.Id;
foreach (var source in sources)
{
    solution = solution.AddDocument(DocumentId.CreateNewId(projectId), Path.GetFileName(source),
        SourceText.From(File.ReadAllText(source), Encoding.UTF8), filePath: source);
}

var original = solution;
var diagnostics = await GetDiagnosticsAsync(original.GetProject(projectId)!, analyzers);
var compileErrors = (await original.GetProject(projectId)!.GetCompilationAsync())!.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
report.AppendLine($"# original: compile errors {compileErrors}, analyzer diagnostics {diagnostics.Length}");
report.AppendLine();

var fixable = fixers.SelectMany(f => f.FixableDiagnosticIds.Select(id => (id, f))).ToLookup(x => x.id, x => x.f);
var counts = new SortedDictionary<string, (int Seen, int Offered, int Compiles)>(StringComparer.Ordinal);
foreach (var diagnostic in diagnostics.OrderBy(d => d.Location.SourceTree!.FilePath, StringComparer.Ordinal).ThenBy(d => d.Location.SourceSpan.Start))
{
    var c = counts.GetValueOrDefault(diagnostic.Id);
    c.Seen++;
    var document = original.GetDocument(diagnostic.Location.SourceTree)!;
    var line = diagnostic.Location.GetLineSpan();
    var where = $"{Path.GetFileName(line.Path)}:{line.StartLinePosition.Line + 1}";
    var actions = new List<CodeAction>();
    foreach (var fixer in fixable[diagnostic.Id])
    {
        var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
        await fixer.RegisterCodeFixesAsync(context);
    }

    if (actions.Count == 0)
    {
        report.AppendLine($"{where} {diagnostic.Id}: no fix offered");
        counts[diagnostic.Id] = c;
        continue;
    }

    c.Offered++;
    foreach (var action in Flatten(actions))
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().FirstOrDefault()?.ChangedSolution;
        if (changed is null)
        {
            report.AppendLine($"{where} {diagnostic.Id}: '{action.Title}' produced no solution change");
            continue;
        }

        var errors = (await changed.GetProject(projectId)!.GetCompilationAsync())!.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id}: {d.GetMessage()}").ToArray();
        var remaining = (await GetDiagnosticsAsync(changed.GetProject(projectId)!, analyzers))
            .Count(d => d.Id == diagnostic.Id && d.Location.GetLineSpan().StartLinePosition.Line == line.StartLinePosition.Line);
        if (errors.Length == 0)
        {
            c.Compiles++;
        }

        report.AppendLine($"{where} {diagnostic.Id}: '{action.Title}' -> compiles: {(errors.Length == 0 ? "yes" : "NO")}, same diagnostic left on line: {remaining}");
        foreach (var docId in changed.GetChanges(original).GetProjectChanges().SelectMany(p => p.GetChangedDocuments()))
        {
            // Line diff: trim the common prefix and suffix, print what is left on each side.
            var before = (await original.GetDocument(docId)!.GetTextAsync()).Lines.Select(l => l.ToString()).ToArray();
            var after = (await changed.GetDocument(docId)!.GetTextAsync()).Lines.Select(l => l.ToString()).ToArray();
            var head = 0;
            while (head < before.Length && head < after.Length && before[head] == after[head])
            {
                head++;
            }

            var tail = 0;
            while (tail < before.Length - head && tail < after.Length - head && before[^(tail + 1)] == after[^(tail + 1)])
            {
                tail++;
            }

            var label = docId == document.Id ? "" : $" ({changed.GetDocument(docId)!.Name})";
            for (var i = head; i < before.Length - tail; i++)
            {
                report.AppendLine($"    -{label} {before[i].Trim()}");
            }

            for (var i = head; i < after.Length - tail; i++)
            {
                report.AppendLine($"    +{label} {after[i].Trim()}");
            }
        }

        foreach (var error in errors.Take(3))
        {
            report.AppendLine($"      error {error}");
        }
    }

    counts[diagnostic.Id] = c;
}

report.AppendLine();
report.AppendLine("# per rule: diagnostics / with a fix offered / fixes that compile");
foreach (var (id, c) in counts)
{
    report.AppendLine($"{id}: {c.Seen} / {c.Offered} / {c.Compiles}");
}

var reportPath = Path.Combine(outputDir, "codefix-report.txt");
File.WriteAllText(reportPath, report.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
Console.WriteLine(report.ToString());
return 0;

static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Project project, ImmutableArray<DiagnosticAnalyzer> analyzers)
{
    var compilation = (await project.GetCompilationAsync())!;
    var withAnalyzers = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
        new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false));
    var result = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    return [.. result.Where(d => d.Id.StartsWith("MSBuildTask", StringComparison.Ordinal))];
}

static IEnumerable<CodeAction> Flatten(IEnumerable<CodeAction> actions)
{
    foreach (var action in actions)
    {
        if (action.NestedActions.IsDefaultOrEmpty)
        {
            yield return action;
        }
        else
        {
            foreach (var nested in Flatten(action.NestedActions))
            {
                yield return nested;
            }
        }
    }
}
