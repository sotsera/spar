using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bench.Generator;

/// <summary>Emits the generated serializers and a per-contract codec. Copied from the D1 spike and trimmed to serialization.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ContractGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.SyntaxProvider.ForAttributeWithMetadataName(
            ContractReader.ContractAttribute,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
            static (ctx, ct) => ContractReader.Read((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0], ct));

        context.RegisterSourceOutput(contracts, static (spc, contract) =>
        {
            foreach (var diagnostic in contract.Diagnostics)
            {
                spc.ReportDiagnostic(ToDiagnostic(diagnostic));
            }

            if (contract.Diagnostics.Count == 0)
            {
                spc.AddSource($"{contract.TypeName}Codec.g.cs", CSharpEmitter.EmitCodec(contract));
            }
        });

        context.RegisterSourceOutput(contracts.Collect(), static (spc, all) =>
        {
            var dtos = all.Where(c => c.Diagnostics.Count == 0).SelectMany(c => c.Dtos).GroupBy(d => d.CSharp)
                .Select(g => g.First()).OrderBy(d => d.CSharp, System.StringComparer.Ordinal);
            spc.AddSource("SparSerializers.g.cs", CSharpEmitter.EmitSerializers(dtos));
        });
    }

    private static Diagnostic ToDiagnostic(DiagnosticModel model)
    {
        var descriptor = new DiagnosticDescriptor(model.Id, "Unsupported contract", "{0}", "Spar", DiagnosticSeverity.Error, true);
        var location = model.Location is { } l
            ? Location.Create(l.Path, new TextSpan(l.Start, l.Length),
                new LinePositionSpan(new LinePosition(l.Line, l.Column), new LinePosition(l.EndLine, l.EndColumn)))
            : Location.None;
        return Diagnostic.Create(descriptor, location, model.Message);
    }
}
