using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// Reports an expression that produces <c>default(Result)</c>: the <c>default</c> literal or expression, and a
/// parameterless <c>new()</c>, when their type is Spar's <c>Result</c> struct. Generic <c>default(T)</c>, array
/// elements and fields that are never assigned cannot be seen here; the result throws on them at run time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultResultAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "SPAR050",
        title: "default(Result) is neither a value nor an error",
        messageFormat: "'{0}' is neither a value nor an error. Return a value, an error case, or Result.Success.",
        category: "Spar.Contracts",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeDefault, OperationKind.DefaultValue);
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeDefault(OperationAnalysisContext context)
    {
        var type = context.Operation.Type;
        if (IsResultStruct(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(), type!.ToDisplayString()));
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Arguments.Length == 0 && creation.Initializer is null && IsResultStruct(creation.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation(), creation.Type!.ToDisplayString()));
        }
    }

    private static bool IsResultStruct(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Struct, Name: "Result", Arity: 1 or 2 } named
        && named.ContainingNamespace is { Name: "Spar", ContainingNamespace.IsGlobalNamespace: true };
}
