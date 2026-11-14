using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// Reports the ways a default Result can be created or observed (SPAR050–SPAR054). See <see cref="Descriptors"/>.
/// Generated code is not analysed: its author, not the user, can fix it, and the runtime guard still throws.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultResultAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        Descriptors.DefaultResult,
        Descriptors.MayReturnDefault,
        Descriptors.DefaultElements,
        Descriptors.UnassignedMember,
        Descriptors.GenericDefault,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // A project that cannot see Spar's Result has nothing to report: register nothing.
        var compilation = context.Compilation;
        if (compilation.GetTypeByMetadataName("Spar.Result`2") is null
            && compilation.GetTypeByMetadataName("Spar.Result`1") is null)
        {
            return;
        }

        var types = new ResultTypes();
        var calls = new MaybeDefaultCalls(types);
        var generics = new GenericDefaults(types, calls);

        context.RegisterOperationAction(c => AnalyzeDefault(c, types), OperationKind.DefaultValue);
        context.RegisterOperationAction(c => AnalyzeObjectCreation(c, types), OperationKind.ObjectCreation);
        context.RegisterOperationAction(c => AnalyzeArrayCreation(c, types), OperationKind.ArrayCreation);
        context.RegisterOperationAction(c => AnalyzeInvocation(c, types, calls), OperationKind.Invocation);
        context.RegisterOperationAction(c => MemberAssignment.AnalyzeStaticRead(c, types), OperationKind.FieldReference);
        context.RegisterOperationAction(c => KnownApis.AnalyzePropertyWrite(c, types), OperationKind.PropertyReference);
        context.RegisterSymbolStartAction(c => MemberAssignment.OnTypeStart(c, types), SymbolKind.NamedType);
        generics.Register(context);
    }

    private static void AnalyzeDefault(OperationAnalysisContext context, ResultTypes types)
    {
        var operation = context.Operation;

        // An omitted optional argument is an implicit default. It is reported at the parameter when the parameter is
        // in source, and at the call (AnalyzeInvocation) when it comes from metadata.
        if (operation.IsImplicit || !types.HoldsResult(operation.Type))
        {
            return;
        }

        if (DefaultUse.IsDeclaredOrCompared(operation, context.ContainingSymbol))
        {
            return;
        }

        Report(context, Descriptors.DefaultResult, operation.Syntax, types.DescribeDefault(operation.Type!));
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, ResultTypes types)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var type = creation.Type;
        if (type is null)
        {
            return;
        }

        if (KnownApis.AnalyzeCreation(context, creation, types))
        {
            return;
        }

        if (ConstraintBinding.Report(context, creation, types))
        {
            return;
        }

        // new S() on a struct without an explicit parameterless constructor is default(S).
        var zeroInit = creation.Constructor is null or { IsImplicitlyDeclared: true, Parameters.Length: 0 };
        if (!zeroInit || !types.HoldsResult(type))
        {
            return;
        }

        // An object initializer may set every member that holds a Result (never possible on Result itself).
        if (!ResultTypes.IsResult(type) && InitializerAssignsEveryResultMember(creation, types))
        {
            return;
        }

        Report(context, Descriptors.DefaultResult, creation.Syntax, types.DescribeDefault(type));
    }

    private static void AnalyzeArrayCreation(OperationAnalysisContext context, ResultTypes types)
    {
        var creation = (IArrayCreationOperation)context.Operation;
        if (creation.Type is not IArrayTypeSymbol array || !types.HoldsResult(array.ElementType))
        {
            return;
        }

        // With an initializer the compiler requires one element per slot.
        if (creation.Initializer is not null || IsEmpty(creation) || FillLoop.FillsEveryElement(creation))
        {
            return;
        }

        Report(context, Descriptors.DefaultElements, creation.Syntax, ResultTypes.Display(array.ElementType));
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ResultTypes types, MaybeDefaultCalls calls)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (KnownApis.AnalyzeInvocation(context, invocation, types))
        {
            return;
        }

        if (ConstraintBinding.Report(context, invocation, types))
        {
            return;
        }

        if (calls.AnalyzeInvocation(context, invocation))
        {
            return;
        }

        ReportOmittedMetadataDefault(context, invocation, types);
    }

    /// <summary>A call that omits an optional Result parameter declared in metadata with a default value.</summary>
    private static void ReportOmittedMetadataDefault(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        ResultTypes types)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.DefaultValue
                || argument.Parameter is not { } parameter
                || !types.HoldsResult(parameter.Type)
                || IsInSource(parameter))
            {
                continue;
            }

            var description = $"{types.DescribeDefault(parameter.Type)} for the omitted argument '{parameter.Name}'";
            Report(context, Descriptors.DefaultResult, invocation.Syntax, description);
            return;
        }
    }

    private static bool InitializerAssignsEveryResultMember(IObjectCreationOperation creation, ResultTypes types)
    {
        if (creation.Initializer is null || creation.Type is not INamedTypeSymbol type)
        {
            return false;
        }

        var assigned = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var initializer in creation.Initializer.Initializers)
        {
            if (initializer is ISimpleAssignmentOperation { Target: IMemberReferenceOperation member })
            {
                assigned.Add(member.Member);
            }
        }

        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false, IsConst: false } field && types.HoldsResult(field.Type))
            {
                var settable = field.AssociatedSymbol ?? field;
                if (!assigned.Contains(settable))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsEmpty(IArrayCreationOperation creation)
    {
        foreach (var size in creation.DimensionSizes)
        {
            if (size.ConstantValue is { HasValue: true, Value: 0 })
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsInSource(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return true;
            }
        }

        return false;
    }

    internal static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, SyntaxNode node, params object[] arguments)
    {
        var text = Shorten(node.ToString());
        var all = new object[arguments.Length + 1];
        all[0] = text;
        arguments.CopyTo(all, 1);
        context.ReportDiagnostic(Diagnostic.Create(rule, node.GetLocation(), all));
    }

    internal static string Shorten(string text)
    {
        var singleLine = text.Replace("\r", string.Empty).Replace('\n', ' ');
        return singleLine.Length <= 60 ? singleLine : singleLine.Substring(0, 57) + "...";
    }
}
