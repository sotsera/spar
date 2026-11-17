using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Spar.Generator;

/// <summary>
/// Shows contract and <c>Program.cs</c> problems at their locations, from the same analysis the generator uses.
/// Every contract of the compilation is read once per compilation, the cross-contract checks run on the result, and
/// each problem is reported by the type that contains it, so it appears in its own document as you type and once,
/// however many contracts share the type it is about.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.NotSealedClassOrInterface,
        Descriptors.GenericContract,
        Descriptors.InvalidName,
        Descriptors.ReservedName,
        Descriptors.ListedLibrary,
        Descriptors.DuplicateContract,
        Descriptors.UnsupportedReturn,
        Descriptors.TooManyRequests,
        Descriptors.TokenNotLast,
        Descriptors.RefParameter,
        Descriptors.GenericCommand,
        Descriptors.UnsupportedRequest,
        Descriptors.DuplicateCommand,
        Descriptors.UnsupportedWireType,
        Descriptors.NotPositionalRecord,
        Descriptors.MissingProperty,
        Descriptors.DuplicateJsonName,
        Descriptors.DateTimeOnWire,
        Descriptors.UnsupportedEnum,
        Descriptors.BadErrorType,
        Descriptors.CaseNotNested,
        Descriptors.BadErrorCase,
        Descriptors.ReservedPayloadName,
        Descriptors.DuplicateCode,
        Descriptors.NoErrorCases,
        Descriptors.DuplicateTypeName,
        Descriptors.NotTopLevel,
        Descriptors.NotReturningRun,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(ContractLibraries.Property, out var listed);
            var libraries = ContractProblems.ParseList(listed);
            var problems = new Lazy<ContractProblems>(() => ContractProblems.Find(start.Compilation, libraries), isThreadSafe: true);
            start.RegisterSymbolAction(
                symbol =>
                {
                    foreach (var diagnostic in problems.Value.In((INamedTypeSymbol)symbol.Symbol))
                    {
                        symbol.ReportDiagnostic(diagnostic);
                    }
                },
                SymbolKind.NamedType);

            // Problems inside a listed library, or with the list, have no place in the project: once, at the end.
            start.RegisterCompilationEndAction(end =>
            {
                foreach (var diagnostic in problems.Value.Unlocated)
                {
                    end.ReportDiagnostic(diagnostic);
                }
            });
        });
        context.RegisterCompilationAction(AnalyzeEntryPoint);
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
