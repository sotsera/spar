using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator;

/// <summary>
/// Shows page security problems at their locations as you type (plan section 3b; SPAR101–SPAR106), from the analysis
/// the generator and the client step use: a <c>ContentSecurity</c> or <c>Permissions</c> call the build cannot read
/// where it sits, and, in each <c>SparApplication.Create</c> configuration, values that are not constants, shapes the
/// build cannot read, and sources the policy refuses; and the window colours the page's theme style needs (SPAR106).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PageSecurityAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.NotDirectlyInCreate,
        Descriptors.NotConstant,
        Descriptors.UnsupportedShape,
        Descriptors.InvalidValue,
        Descriptors.NonceWithInlineStyles,
        Descriptors.WindowColours,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            // Only an app's Program.cs configures Spar: a library or a test project may call the builder freely.
            var isApp = new System.Lazy<bool>(() => PageSecurityAnalysis.IsApp(start.Compilation, start.CancellationToken), isThreadSafe: true);
            start.RegisterSyntaxNodeAction(node => Analyze(node, isApp), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, System.Lazy<bool> isApp)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return;
        }

        switch (access.Name.Identifier.ValueText)
        {
            case "Create" when PageSecurityAnalysis.IsCreate(invocation, context.SemanticModel, context.CancellationToken):
                foreach (var problem in PageSecurityAnalysis.Read(invocation, context.SemanticModel, context.CancellationToken).Problems)
                {
                    context.ReportDiagnostic(problem);
                }

                foreach (var problem in WindowColorsAnalysis.Read(invocation, context.SemanticModel, context.CancellationToken).Problems)
                {
                    context.ReportDiagnostic(problem);
                }

                break;
            case "Background" when isApp.Value:
                if (WindowColorsAnalysis.MisplacedCall(invocation, context.SemanticModel, context.CancellationToken) is { } colours)
                {
                    context.ReportDiagnostic(colours);
                }

                break;
            case "ContentSecurity" or "Permissions" when isApp.Value:
                if (PageSecurityAnalysis.MisplacedCall(invocation, context.SemanticModel, context.CancellationToken) is { } misplaced)
                {
                    context.ReportDiagnostic(misplaced);
                }

                break;
        }
    }
}
