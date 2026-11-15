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
            var problems = new Lazy<Problems>(() => Problems.Find(start.Compilation), isThreadSafe: true);
            start.RegisterSymbolAction(symbol => problems.Value.Report(symbol), SymbolKind.NamedType);
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

    /// <summary>Every contract problem of one compilation, grouped by the type that reports it.</summary>
    private sealed class Problems
    {
        private readonly Dictionary<INamedTypeSymbol, List<Diagnostic>> _byType = new(SymbolEqualityComparer.Default);
        private readonly HashSet<(string Id, string Path, int Start, string Message)> _seen = [];

        public static Problems Find(Compilation compilation)
        {
            var problems = new Problems();
            var symbols = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var models = new List<ContractModel>();
            foreach (var type in Contracts(compilation.Assembly.GlobalNamespace))
            {
                var model = ContractAnalysis.Analyze(type, problems.Add, symbols);
                if (model is not null)
                {
                    models.Add(model);
                }
            }

            foreach (var problem in ContractSet.Check(models).Problems)
            {
                if (symbols.TryGetValue(problem.Subject, out var subject))
                {
                    var location = subject.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
                    problems.Add(subject, Diagnostic.Create(problem.Descriptor, location, problem.Arguments));
                }
            }

            return problems;
        }

        public void Report(SymbolAnalysisContext context)
        {
            if (_byType.TryGetValue((INamedTypeSymbol)context.Symbol, out var diagnostics))
            {
                foreach (var diagnostic in diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        /// <summary>
        /// Files a diagnostic under the type its symbol belongs to (a parameter or method under its type), once per
        /// location and message. Problems with no source location (inside referenced assemblies) are the generator's.
        /// </summary>
        private void Add(ISymbol symbol, Diagnostic diagnostic)
        {
            var owner = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            var location = diagnostic.Location;
            if (owner is null || !location.IsInSource)
            {
                return;
            }

            var key = (diagnostic.Id, location.SourceTree!.FilePath, location.SourceSpan.Start, diagnostic.GetMessage());
            if (!_seen.Add(key))
            {
                return;
            }

            if (!_byType.TryGetValue(owner, out var list))
            {
                _byType[owner] = list = [];
            }

            list.Add(diagnostic);
        }

        private static IEnumerable<INamedTypeSymbol> Contracts(INamespaceSymbol root)
        {
            var pending = new Stack<INamespaceOrTypeSymbol>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var member in current is INamespaceSymbol ns ? ns.GetMembers() : ((INamedTypeSymbol)current).GetTypeMembers().Cast<INamespaceOrTypeSymbol>())
                {
                    pending.Push(member);
                }

                if (current is INamedTypeSymbol type && IsContract(type))
                {
                    yield return type;
                }
            }
        }

        private static bool IsContract(INamedTypeSymbol type) =>
            type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Spar.SparContractAttribute");
    }
}
