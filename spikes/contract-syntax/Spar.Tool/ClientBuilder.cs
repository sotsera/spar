using System.Collections.Concurrent;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spar.Generator;

namespace Spar.Tool;

/// <summary>
/// Client generation, shared by the command-line tool and the MSBuild task. All paths must be absolute; callers
/// resolve them (the task through TaskEnvironment).
/// </summary>
public static class ClientBuilder
{
    // Metadata references are the expensive part of analysis. A long-lived host reuses them while the file's
    // timestamp and size are unchanged.
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, long Length, MetadataReference Reference)> References =
        new(StringComparer.OrdinalIgnoreCase);

    public static (int Contracts, int Files, int Written) GenerateClient(string inventoryPath, string outDir)
    {
        var lines = File.ReadAllLines(inventoryPath);
        string Value(string key) => lines.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? string.Empty;
        IEnumerable<string> Values(string key) => lines.Where(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Select(l => l[(key.Length + 1)..]);

        // Roslyn 5.9 knows C# 15's closed classes only as a preview feature; the SDK compiler does the real compile.
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithPreprocessorSymbols(Value("define").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var trees = Values("source").Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path)).ToList();

        var references = new List<MetadataReference>();
        foreach (var path in Values("reference"))
        {
            var info = new FileInfo(path);
            if (References.TryGetValue(path, out var cached) && cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                references.Add(cached.Reference);
                continue;
            }

            var reference = MetadataReference.CreateFromFile(path);
            References[path] = (info.LastWriteTimeUtc, info.Length, reference);
            references.Add(reference);
        }

        var nullable = Value("nullable") is "enable" or "Enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable;
        var compilation = CSharpCompilation.Create(Value("assembly"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable));

        // The analyzer reports contract diagnostics with source locations during compilation; this step only leaves
        // invalid contracts and commands out, so the C# compiler, not this step, owns the error report.
        var set = ContractSet.Analyze(ContractDiscovery.ReadLocal(compilation, CancellationToken.None)
            .Concat(ContractDiscovery.ReadReferenced(compilation, CancellationToken.None)));

        var files = ClientEmitter.EmitPackages(set.Contracts);
        files["contracts.json"] = ClientEmitter.EmitManifest(set.Contracts);

        Directory.CreateDirectory(outDir);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = 0;
        foreach (var (name, content) in files)
        {
            var path = Path.GetFullPath(Path.Combine(outDir, name));
            owned.Add(path);
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content, new UTF8Encoding(false));
                written++;
            }
        }

        // The client folder is owned by this step: remove modules of contracts and types that no longer exist.
        foreach (var stale in Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Where(f => !owned.Contains(Path.GetFullPath(f))))
        {
            File.Delete(stale);
        }

        foreach (var empty in Directory.EnumerateDirectories(outDir, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length).Where(d => !Directory.EnumerateFileSystemEntries(d).Any()))
        {
            Directory.Delete(empty);
        }

        return (set.Contracts.Count, files.Count, written);
    }
}
