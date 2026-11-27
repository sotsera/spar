using System.Collections.Concurrent;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Spar.Generator;

namespace Spar.Sdk.Tasks.Client;

/// <summary>What the project's build passes the client step: its sources, references and compile settings.</summary>
internal sealed record ClientInputs(
    AbsolutePath ProjectDirectory,
    IReadOnlyList<AbsolutePath> Sources,
    IReadOnlyList<AbsolutePath> References,
    IReadOnlyList<string> Defines,
    bool NullableEnabled,
    IReadOnlyList<string> Libraries);

/// <summary>One contract as the generator sees it, with the C# documentation and the file that declares it.</summary>
internal sealed record ClientContract(ContractModel Model, string SourceFile, ClientDocs Docs);

/// <summary>
/// The valid contracts, the page security, the window colours and the window kinds Program.cs declares (null without a
/// Create call), and every problem the analysis found: the same diagnostics the analyzers report.
/// </summary>
internal sealed record ContractRead(
    IReadOnlyList<ClientContract> Contracts,
    IReadOnlyList<Diagnostic> Problems,
    PageSecurityModel? PageSecurity = null,
    WindowColorsModel? WindowColors = null,
    IReadOnlyList<WindowKindName>? WindowKinds = null);

/// <summary>
/// Reads the project's contracts with the generator's own analysis (decision "D1: Compilation pipeline"), so the page's
/// modules and fingerprint match the generated C# exactly. Parses with the Roslyn Spar references, at the preview
/// language version: a file in syntax that Roslyn does not know only loses what it cannot parse.
/// </summary>
internal static class ContractSource
{
    /// <summary>Metadata references by file identity, kept across builds in a long-lived MSBuild node or task host.</summary>
    private static readonly ConcurrentDictionary<ReferenceKey, MetadataReference> ReferenceCache = new();

    public static ContractRead Read(ClientInputs inputs, CancellationToken cancellationToken) =>
        Read(Compile(inputs, cancellationToken), inputs.ProjectDirectory, inputs.Libraries, cancellationToken);

    /// <summary>
    /// The contracts of a compilation and their problems, read by the analyzer's own code (<see cref="ContractProblems"/>);
    /// source paths in the headers are relative to <paramref name="projectDirectory"/>.
    /// </summary>
    public static ContractRead Read(CSharpCompilation compilation, string projectDirectory, IReadOnlyList<string> libraries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var docs = new ClientDocs(compilation, projectDirectory, libraries);
        var found = ContractProblems.Find(compilation, libraries);

        var contracts = new List<ClientContract>(found.Valid.Count);
        foreach (var (model, type) in found.Valid)
        {
            var file = type.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree?.FilePath;
            var source = file is null ? $"{type.ContainingAssembly.Name} (a referenced library)" : RelativePath(projectDirectory, file);
            contracts.Add(new ClientContract(model, source, docs));
        }

        // The page security, read by the same analysis as the generator's and the analyzer's (plan section 3b).
        var security = PageSecurityAnalysis.Find(compilation, cancellationToken);

        // The window colours, for the page's theme style (plan section 3d).
        var colours = WindowColorsAnalysis.Find(compilation, cancellationToken);

        // The window kinds, for the page's WindowKind union.
        var kinds = WindowKindsAnalysis.Find(compilation, cancellationToken);

        // Warnings, such as DateTime on the wire, leave the contract valid; the compiler's analyzer reports them.
        var errors = found.All
            .Concat(security?.Problems ?? [])
            .Concat(colours?.Problems ?? [])
            .Concat(kinds?.Problems ?? [])
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        var ordered = contracts.OrderBy(c => c.Model.WireName, StringComparer.Ordinal).ToList();
        return new ContractRead(ordered, errors, security?.Model, colours?.Model, kinds?.Kinds);
    }

    private static CSharpCompilation Compile(ClientInputs inputs, CancellationToken cancellationToken)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: inputs.Defines);
        var trees = new List<SyntaxTree>(inputs.Sources.Count);
        foreach (var source in inputs.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(source);
            var text = SourceText.From(stream, Encoding.UTF8);
            trees.Add(CSharpSyntaxTree.ParseText(text, parse, source, cancellationToken));
        }

        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            nullableContextOptions: inputs.NullableEnabled ? NullableContextOptions.Enable : NullableContextOptions.Disable);
        var references = inputs.References.Select(Reference).ToList();
        return CSharpCompilation.Create("SparClient", trees, references, options);
    }

    /// <summary>
    /// The assembly with the XML documentation file beside it, as the compiler finds it, so a listed library's comments
    /// reach the JSDoc. <see cref="XmlDocumentationFile"/> reads that file only when a symbol's documentation is asked for.
    /// </summary>
    internal static MetadataReference Reference(AbsolutePath path)
    {
        var file = new FileInfo(path);
        var docs = new FileInfo(Path.ChangeExtension(file.FullName, ".xml"));
        var key = docs.Exists
            ? new ReferenceKey(file.FullName, file.LastWriteTimeUtc, file.Length, docs.LastWriteTimeUtc, docs.Length)
            : new ReferenceKey(file.FullName, file.LastWriteTimeUtc, file.Length, default, -1);
        return ReferenceCache.GetOrAdd(key, static (key, docs) => MetadataReference.CreateFromFile(
            key.Path,
            documentation: docs.Exists ? new XmlDocumentationFile(docs.FullName) : null), docs);
    }

    internal static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                foreach (var type in AllTypes(nested))
                {
                    yield return type;
                }

                continue;
            }

            foreach (var type in WithNested((INamedTypeSymbol)member))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers().SelectMany(WithNested))
        {
            yield return nested;
        }
    }

    private static string RelativePath(string projectDirectory, string file) =>
        file.Length == 0 ? "" : Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');

    /// <summary>A reference's identity: the assembly file and its documentation file, either of which may change.</summary>
    private readonly record struct ReferenceKey(string Path, DateTime Written, long Length, DateTime DocsWritten, long DocsLength);
}
