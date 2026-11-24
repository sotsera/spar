using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator;

/// <summary>
/// Hides CA1822 ("Member does not access instance data and can be marked as static") on the commands of a contract
/// class. A command runs on the contract's instance from the call's scope, and a static <c>[SparCommand]</c> method is
/// not a command at all, so following the advice would remove the command from the page without an error. Other
/// methods, including the contract class's own helpers, keep the diagnostic.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommandStaticSuppressor : DiagnosticSuppressor
{
    /// <summary>The suppression, as the IDE and the build list it.</summary>
    internal static readonly SuppressionDescriptor MarkMembersAsStatic = new(
        id: "SPARSUP001",
        suppressedDiagnosticId: "CA1822",
        justification: "A contract command runs on the contract's instance; a static [SparCommand] method is not a command.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = [MarkMembersAsStatic];

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (IsOnCommand(diagnostic, context))
            {
                context.ReportSuppression(Suppression.Create(MarkMembersAsStatic, diagnostic));
            }
        }
    }

    private static bool IsOnCommand(Diagnostic diagnostic, SuppressionAnalysisContext context)
    {
        var tree = diagnostic.Location.SourceTree;
        if (tree is null)
        {
            return false;
        }

        var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
        var declaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (declaration is null)
        {
            return false;
        }

        var model = context.GetSemanticModel(tree);
        var method = model.GetDeclaredSymbol(declaration, context.CancellationToken);
        return method is not null
            && method.ContainingType is { TypeKind: TypeKind.Class } type
            && HasSparAttribute(type, "SparContractAttribute")
            && HasSparAttribute(method, "SparCommandAttribute");
    }

    private static bool HasSparAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.Name == name && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Spar");
}
