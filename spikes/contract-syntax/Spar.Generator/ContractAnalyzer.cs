using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Spar.Generator;

/// <summary>
/// Reports contract diagnostics on every edit, through the same analysis code as the generator, so both IDEs draw a
/// squiggle. Each document gets the diagnostics located in it; the contract set is analysed once per compilation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SparDiagnostics.All;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ContractReader.ContractAttribute) is null)
            {
                return;
            }

            var compilation = start.Compilation;
            var cancellationToken = start.CancellationToken;
            var set = new Lazy<ContractSet>(() => ContractSet.Analyze(
                ContractDiscovery.ReadLocal(compilation, cancellationToken)
                    .Concat(ContractDiscovery.ReadReferenced(compilation, cancellationToken))));

            start.RegisterSemanticModelAction(model =>
            {
                var tree = model.SemanticModel.SyntaxTree;
                foreach (var diagnostic in set.Value.Diagnostics.Where(d => d.Location?.Path == tree.FilePath))
                {
                    var location = diagnostic.Location!;
                    var span = new TextSpan(location.Start, location.Length);
                    if (span.End <= tree.Length)
                    {
                        model.ReportDiagnostic(Diagnostic.Create(SparDiagnostics.Get(diagnostic.Id), Location.Create(tree, span), diagnostic.Message));
                    }
                }
            });
        });
    }
}
