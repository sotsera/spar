using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Spike.Generator;

/// <summary>Emits all contract C#: dispatch, serializers, catalog and manifest. Diagnostics surface live in the IDE.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ContractGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.SyntaxProvider.ForAttributeWithMetadataName(
            ContractReader.ContractAttribute,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
            static (ctx, ct) => ContractReader.Read((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0], ct));

        var mode = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.SpikeSerializer", out var value)
                && string.Equals(value, "Stj", System.StringComparison.OrdinalIgnoreCase)
                ? SerializerMode.Stj
                : SerializerMode.Spar);

        context.RegisterSourceOutput(contracts.Combine(mode), static (spc, pair) =>
        {
            var (contract, serializer) = pair;
            foreach (var diagnostic in contract.Diagnostics)
            {
                spc.ReportDiagnostic(ToDiagnostic(diagnostic));
            }

            if (contract.Diagnostics.Count == 0)
            {
                spc.AddSource($"{contract.TypeName}Binding.g.cs", CSharpEmitter.EmitBinding(contract, serializer));
            }
        });

        context.RegisterSourceOutput(contracts.Collect().Combine(mode), static (spc, pair) =>
        {
            var (all, serializer) = pair;
            var valid = all.Where(c => c.Diagnostics.Count == 0).OrderBy(c => c.WireName, System.StringComparer.Ordinal).ToList();
            spc.AddSource("SparCatalog.g.cs", CSharpEmitter.EmitCatalog(valid, ClientEmitter.EmitManifest(valid)));
            if (serializer == SerializerMode.Spar)
            {
                var dtos = valid.SelectMany(c => c.Dtos).GroupBy(d => d.CSharp).Select(g => g.First())
                    .OrderBy(d => d.CSharp, System.StringComparer.Ordinal);
                spc.AddSource("SparSerializers.g.cs", CSharpEmitter.EmitSerializers(dtos));
            }
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
