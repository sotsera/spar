using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator;

/// <summary>Shows contract and <c>Program.cs</c> problems at their locations, from the same analysis the generator uses.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.NotSealedClassOrInterface,
        Descriptors.GenericContract,
        Descriptors.UnsupportedReturn,
        Descriptors.TooManyRequests,
        Descriptors.TokenNotLast,
        Descriptors.RefParameter,
        Descriptors.GenericCommand,
        Descriptors.UnsupportedWireType,
        Descriptors.NotPositionalRecord,
        Descriptors.BadErrorType,
        Descriptors.ReservedPayloadName,
        Descriptors.NoErrorCases,
        Descriptors.NotTopLevel,
        Descriptors.NotReturningRun,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        context.RegisterCompilationAction(AnalyzeEntryPoint);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var isContract = type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Spar.SparContractAttribute");
        if (isContract)
        {
            ContractAnalysis.Analyze(type, context.ReportDiagnostic);
        }
    }

    /// <summary>An app's Program.cs is top-level statements ending with <c>return app.Run();</c> (plan section 2).</summary>
    private static void AnalyzeEntryPoint(CompilationAnalysisContext context)
    {
        var usesSpar = context.Compilation.GetTypeByMetadataName("Spar.SparApplication") is not null;
        var method = context.Compilation.GetEntryPoint(context.CancellationToken);
        if (!usesSpar || method is null || !CallsCreate(context.Compilation))
        {
            return;
        }

        var location = method.ContainingType.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
        if (method.Name != WellKnownMemberNames.TopLevelStatementsEntryPointMethodName)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.NotTopLevel, method.Locations.FirstOrDefault() ?? location, method.ToDisplayString()));
            return;
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Int32)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.NotReturningRun, location, method.ReturnType.ToDisplayString()));
        }
    }

    // Only apps that compose with SparApplication.Create; a test project or a library referencing Spar is left alone.
    private static bool CallsCreate(Compilation compilation) =>
        compilation.SyntaxTrees.Any(tree => tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>()
            .Any(name => name.Identifier.ValueText == "SparApplication"));
}
