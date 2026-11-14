using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Spar.Analyzers;
using Spar.Analyzers.CodeFixes;

namespace Spar050.Harness;

/// <summary>
/// Applies each code fix to a small document in an AdhocWorkspace (Roslyn 5.9, the version the analyzer targets),
/// then recompiles: the diagnostic must be gone and no new compiler error may appear.
/// </summary>
internal static class CodeFixCheck
{
    private const string GlobalUsings =
        "global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n"
        + "global using System.Threading.Tasks;\n";

    private static readonly (string Name, string Id, string FixTitle, string Code)[] s_cases =
    [
        ("Result<TError> => default", "SPAR050", "Use Result.Success",
            "public static class A { public static CancelResult M() => default; }"),
        ("Result<TError> default, error case", "SPAR050", "Use error case 'CancelError.Forbidden'",
            "public static class A { public static CancelResult M() => default; }"),
        ("var x = default(Result<TError>)", "SPAR050", "Use Result.Success",
            "public static class A { public static CancelResult M() { var x = default(CancelResult); return x; } }"),
        ("return new() in async", "SPAR050", "Use Result.Success",
            "public static class A { public static async Task<CancelResult> M() { await Task.Yield(); return new(); } }"),
        ("Result<T, TError> default: no case without arguments", "SPAR050", "",
            "public static class A { public static PlanResult M() => default; }"),
        ("FirstOrDefault()", "SPAR051", "Use First() (throws when nothing is found)",
            "public static class A { public static PlanResult M(IEnumerable<PlanResult> r) => r.FirstOrDefault(); }"),
        ("SingleOrDefault(predicate)", "SPAR051", "Use Single() (throws when nothing is found)",
            "public static class A { public static PlanResult M(IEnumerable<PlanResult> r) => r.SingleOrDefault(x => x.IsError); }"),
        ("Nullable GetValueOrDefault()", "SPAR051", "Use .Value (throws when there is no value)",
            "public static class A { public static PlanResult M(PlanResult? r) => r.GetValueOrDefault(); }"),
        ("field not assigned by the constructor", "SPAR053", "Declare as Result? (unset is null)",
            "public sealed class H { private PlanResult _last; public void Set(PlanResult v) => _last = v; "
            + "public override string ToString() => _last.ToString(); }"),
        ("auto-property, make required", "SPAR053", "Make the property required",
            "public sealed class H { public PlanResult Last { get; init; } }"),
    ];

    public static async Task<int> RunAsync(string spikeRoot)
    {
        var shared = new[]
        {
            ("Result.cs", await File.ReadAllTextAsync(Path.Combine(spikeRoot, "Result", "Result.cs"))),
            ("Domain.cs", await File.ReadAllTextAsync(Path.Combine(spikeRoot, "Corpus", "Shared", "Domain.cs"))),
            ("Usings.cs", await File.ReadAllTextAsync(Path.Combine(spikeRoot, "Corpus", "Shared", "Usings.cs"))),
            ("GlobalUsings.cs", GlobalUsings),
        };
        var references = PlatformReferences();
        var failures = 0;

        Console.WriteLine($"# Code fixes, Roslyn {typeof(CSharpCompilation).Assembly.GetName().Version} in an AdhocWorkspace");
        foreach (var (name, id, fixTitle, code) in s_cases)
        {
            var outcome = await CheckAsync(shared, references, id, fixTitle, code);
            failures += outcome.Passed ? 0 : 1;
            Console.WriteLine($"{(outcome.Passed ? "pass" : "FAIL")} | {name} | {id} | offered: {outcome.Offered}");
            Console.WriteLine($"    before: {code}");
            Console.WriteLine($"    after:  {outcome.After}");
            if (outcome.Note.Length > 0)
            {
                Console.WriteLine($"    note:   {outcome.Note}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static async Task<Outcome> CheckAsync(
        (string Name, string Text)[] shared,
        ImmutableArray<MetadataReference> references,
        string id,
        string fixTitle,
        string code)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var info = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Probe",
            "Probe",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            metadataReferences: references);
        var solution = workspace.CurrentSolution.AddProject(info);
        foreach (var (name, text) in shared)
        {
            solution = solution.AddDocument(DocumentId.CreateNewId(projectId), name, SourceText.From(text), filePath: name);
        }

        var caseId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(caseId, "Case.cs", SourceText.From("namespace Probe;\n" + code), filePath: "Case.cs");
        var document = solution.GetDocument(caseId)!;

        var (errorsBefore, diagnostics) = await AnalyzeAsync(document.Project, id);
        if (diagnostics.Length == 0)
        {
            return new Outcome(false, "-", code, $"no {id} before the fix");
        }

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostics[0], (action, _) => actions.Add(action), CancellationToken.None);
        await new DefaultResultCodeFix().RegisterCodeFixesAsync(context);
        var offered = actions.Count == 0 ? "none" : string.Join("; ", actions.Select(a => a.Title));

        if (fixTitle.Length == 0)
        {
            // The case expects no fix: Result<T, TError> needs a value or an error with arguments only the author knows.
            return new Outcome(actions.Count == 0, offered, code, "no fix expected");
        }

        var chosen = actions.FirstOrDefault(a => a.Title == fixTitle);
        if (chosen is null)
        {
            return new Outcome(false, offered, code, $"'{fixTitle}' not offered");
        }

        var operations = await chosen.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var fixedDocument = changed.GetDocument(caseId)!;
        var after = (await fixedDocument.GetTextAsync()).ToString().Replace("namespace Probe;\n", string.Empty).Trim();

        var (errorsAfter, remaining) = await AnalyzeAsync(fixedDocument.Project, id);
        var newErrors = errorsAfter.Except(errorsBefore).ToArray();
        var passed = remaining.Length == 0 && newErrors.Length == 0;
        var note = newErrors.Length == 0 ? string.Empty : "new errors: " + string.Join(" | ", newErrors);
        if (remaining.Length > 0)
        {
            note += $" {id} still reported";
        }

        note += $" (compiler errors in the probe before/after: {errorsBefore.Count}/{errorsAfter.Count})";
        return new Outcome(passed, offered, after, note.Trim());
    }

    private static async Task<(HashSet<string> Errors, ImmutableArray<Diagnostic> Diagnostics)> AnalyzeAsync(Project project, string id)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToHashSet();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DefaultResultAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        var inCase = all.Where(d => d.Id == id && d.Location.SourceTree?.FilePath == "Case.cs").ToImmutableArray();
        return (errors, inCase);
    }

    private static ImmutableArray<MetadataReference> PlatformReferences()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return
        [
            .. paths
                .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal)
                    || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
        ];
    }

    private sealed record Outcome(bool Passed, string Offered, string After, string Note);
}
