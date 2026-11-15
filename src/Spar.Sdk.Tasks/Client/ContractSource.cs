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
    bool NullableEnabled);

/// <summary>One contract as the generator sees it, with the C# documentation and the file that declares it.</summary>
internal sealed record ClientContract(ContractModel Model, string SourceFile, ClientDocs Docs);

/// <summary>
/// Reads the project's contracts with the generator's own analysis (decision "D1: Compilation pipeline"), so the page's
/// modules and fingerprint match the generated C# exactly. Parses with the Roslyn Spar references, at the preview
/// language version: a file in syntax that Roslyn does not know only loses what it cannot parse.
/// </summary>
internal static class ContractSource
{
    /// <summary>Metadata references by file identity, kept across builds in a long-lived MSBuild node or task host.</summary>
    private static readonly ConcurrentDictionary<(string Path, DateTime Written, long Length), MetadataReference> ReferenceCache = new();

    public static IReadOnlyList<ClientContract> Read(ClientInputs inputs, CancellationToken cancellationToken) =>
        Read(Compile(inputs, cancellationToken), inputs.ProjectDirectory, cancellationToken);

    /// <summary>The contracts of a compilation; source paths in the headers are relative to <paramref name="projectDirectory"/>.</summary>
    public static IReadOnlyList<ClientContract> Read(CSharpCompilation compilation, string projectDirectory, CancellationToken cancellationToken)
    {
        var docs = new ClientDocs(compilation);

        var contracts = new List<ClientContract>();
        foreach (var type in ContractTypes(compilation, cancellationToken))
        {
            var model = ContractAnalysis.Analyze(type);
            if (model is null)
            {
                continue; // The analyzer reports why at the declaration; the build fails there.
            }

            var file = type.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree?.FilePath ?? "";
            contracts.Add(new ClientContract(model, RelativePath(projectDirectory, file), docs));
        }

        return [.. contracts.OrderBy(c => c.Model.WireName, StringComparer.Ordinal)];
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

    private static MetadataReference Reference(AbsolutePath path)
    {
        var file = new FileInfo(path);
        return ReferenceCache.GetOrAdd((file.FullName, file.LastWriteTimeUtc, file.Length), key => MetadataReference.CreateFromFile(key.Path));
    }

    /// <summary>Classes and interfaces of this project marked <c>[SparContract]</c>, as the generator selects them.</summary>
    private static IEnumerable<INamedTypeSymbol> ContractTypes(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        var attribute = compilation.GetTypeByMetadataName("Spar.SparContractAttribute");
        if (attribute is null)
        {
            yield break;
        }

        foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marked = type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
            if (marked && type.TypeKind is TypeKind.Class or TypeKind.Interface)
            {
                yield return type;
            }
        }
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
}
