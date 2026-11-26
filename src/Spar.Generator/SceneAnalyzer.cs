using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator;

/// <summary>
/// The scenes' located diagnostics (<see cref="SceneAnalysis"/>): a scene method of the wrong shape (SPAR110), a name
/// that is not lower case letters, digits and hyphens (SPAR111), two scenes with one name (SPAR112). The generator
/// leaves such scenes out.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SceneAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [Descriptors.SceneShape, Descriptors.SceneName, Descriptors.SceneDuplicate];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(SceneAnalysis.Attribute) is not { } attributeType)
            {
                return;
            }

            var named = new ConcurrentDictionary<string, ConcurrentBag<Location>>(System.StringComparer.Ordinal);
            start.RegisterSymbolAction(symbol => Analyze(symbol, attributeType, named), SymbolKind.Method);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (var (name, locations) in named.Select(pair => (pair.Key, pair.Value.OrderBy(location => location.SourceSpan.Start).ToList())))
                {
                    if (locations.Count < 2)
                    {
                        continue;
                    }

                    foreach (var location in locations)
                    {
                        end.ReportDiagnostic(Diagnostic.Create(Descriptors.SceneDuplicate, location, name));
                    }
                }
            });
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol attributeType, ConcurrentDictionary<string, ConcurrentBag<Location>> named)
    {
        var method = (IMethodSymbol)context.Symbol;
        var attribute = method.GetAttributes().FirstOrDefault(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attributeType));
        if (attribute is null)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? method.Locations.FirstOrDefault() ?? Location.None;
        if (SceneAnalysis.ShapeProblem(method) is { } problem)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.SceneShape, location, method.Name, problem));
        }

        var name = SceneAnalysis.NameOf(attribute);
        if (name is null || !SceneAnalysis.IsValidName(name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.SceneName, location, name ?? ""));
            return;
        }

        named.GetOrAdd(name, _ => []).Add(location);
    }
}
