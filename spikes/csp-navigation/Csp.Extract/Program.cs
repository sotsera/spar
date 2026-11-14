using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spar.Security.Generator;

// Usage: Csp.Extract <app project folder> <output csp.json>
// Parses the app's sources (no build), binds them against the BCL and the Spar runtime, runs the same analysis as the
// generator and the analyzer, and writes the policy only when its content changes. Exit 1 with located errors.
var timer = Stopwatch.StartNew();
var project = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var separator = Path.DirectorySeparatorChar;
var trees = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal) && !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
    .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), new CSharpParseOptions(LanguageVersion.Preview), f))
    .ToList();

// What the SDK adds for ImplicitUsings in a Microsoft.NET.Sdk project (normally generated into obj/ by the build).
trees.Add(CSharpSyntaxTree.ParseText("""
    global using System;
    global using System.Collections.Generic;
    global using System.IO;
    global using System.Linq;
    global using System.Net.Http;
    global using System.Threading;
    global using System.Threading.Tasks;
    """, new CSharpParseOptions(LanguageVersion.Preview), "ImplicitUsings.g.cs"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
    .Append(typeof(Spar.SparApplication).Assembly.Location)
    .Select(p => MetadataReference.CreateFromFile(p))
    .ToList();
var compilation = CSharpCompilation.Create("extract", trees, references,
    new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
var parsed = timer.Elapsed.TotalMilliseconds;
var (csp, permissions, diagnostics) = PolicyAnalysis.AnalyzeCompilation(compilation, CancellationToken.None);
foreach (var diagnostic in diagnostics)
{
    Console.Error.WriteLine(diagnostic.ToDiagnostic().ToString());
}

if (diagnostics.Count > 0)
{
    return 1;
}

var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["contentSecurityPolicy"] = csp, ["permissionsPolicy"] = permissions },
    new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
var changed = !File.Exists(output) || File.ReadAllText(output) != json;
if (changed)
{
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, json);
}

Console.WriteLine($"csp.json {(changed ? "written" : "unchanged")} in {timer.Elapsed.TotalMilliseconds:F0} ms (parse+bind setup {parsed:F0} ms): {output}");
return 0;
