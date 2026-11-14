using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.Generator;

/// <summary>
/// Emits all contract C#: dispatch, serializers and the catalog with its manifest, for local contracts and for
/// contracts in referenced assemblies. <see cref="ContractAnalyzer"/> reports the diagnostics that have a source
/// location; the generator reports only those without one (contracts read from metadata), so nothing is reported twice.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ContractGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var local = context.SyntaxProvider.ForAttributeWithMetadataName(
            ContractReader.ContractAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, ct) => ContractReader.Read(
                (INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0], ctx.SemanticModel.Compilation, referenced: false, ct));

        // Re-read on every compilation change; the models are equatable, so emission is skipped when nothing changed.
        var referenced = context.CompilationProvider.Select(static (compilation, ct) =>
            ContractDiscovery.ReadReferenced(compilation, ct).ToEquatableArray());

        var set = local.Collect().Combine(referenced).Select(static (pair, _) => ContractSet.Analyze(pair.Left.Concat(pair.Right)));

        context.RegisterSourceOutput(set, static (spc, contracts) =>
        {
            foreach (var diagnostic in contracts.Diagnostics.Where(d => d.Location is null))
            {
                spc.ReportDiagnostic(Diagnostic.Create(SparDiagnostics.Get(diagnostic.Id), Location.None, diagnostic.Message));
            }

            foreach (var (hintName, source) in CSharpEmitter.Emit(contracts))
            {
                spc.AddSource(hintName, source);
            }
        });
    }
}
