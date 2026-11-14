using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// SPAR051: APIs whose signature says they may give default(T), bound to a Result. The signal is the nullable
/// annotation (T? on an unconstrained T, [return: MaybeNull], [MaybeNull]/[MaybeNullWhen] on an out parameter), so it
/// covers metadata and source alike: FirstOrDefault, GetValueOrDefault, List.Find, TryGetValue, TryDequeue…
/// For out arguments only uses that can read the default are reported: an ignored bool result, a read in the branch
/// where the call failed, or a read after an if whose failing branch neither exits nor assigns.
/// </summary>
internal sealed class MaybeDefaultCalls
{
    private readonly ResultTypes _types;

    public MaybeDefaultCalls(ResultTypes types)
    {
        _types = types;
    }

    /// <summary>True when the method's return is declared maybe-default and holds a Result.</summary>
    public bool ReturnsMaybeDefault(IMethodSymbol method)
    {
        if (!_types.HoldsResult(method.ReturnType))
        {
            return false;
        }

        var original = method.OriginalDefinition;
        if (ThrowsForValueTypes(original))
        {
            return false;
        }

        if (original.ReturnType is ITypeParameterSymbol && original.ReturnNullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        return DefaultUse.ReturnsMaybeNull(original);
    }

    public bool AnalyzeInvocation(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var reported = false;
        var method = invocation.TargetMethod;
        if (ReturnsMaybeDefault(method))
        {
            var reason = method.Name.EndsWith("OrDefault", System.StringComparison.Ordinal)
                ? "when it finds nothing"
                : "its return is declared as possibly default";
            var result = _types.FindResult(method.ReturnType, 0)!;
            DefaultResultAnalyzer.Report(context, Descriptors.MayReturnDefault, invocation.Syntax, ResultTypes.Display(result), reason);
            reported = true;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { RefKind: RefKind.Out } parameter
                || !ResultTypes.MayLeaveDefault(parameter, out var whenValue))
            {
                continue;
            }

            var result = _types.FindResult(parameter.Type, 0);
            if (result is not null)
            {
                reported |= AnalyzeOutArgument(context, invocation, argument, whenValue, result);
            }
        }

        return reported;
    }

    internal static bool IsWrite(IOperation reference)
    {
        return reference.Parent switch
        {
            ISimpleAssignmentOperation assignment => assignment.Target == reference,
            ICompoundAssignmentOperation assignment => assignment.Target == reference,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref,
            IDeclarationExpressionOperation => true,
            _ => false,
        };
    }

    private static bool ThrowsForValueTypes(IMethodSymbol method)
    {
        // Min, Max, MinBy and MaxBy are annotated T? but throw on an empty sequence of a non-nullable value type.
        return method.ContainingType.Name == "Enumerable"
            && method.Name is "Min" or "Max" or "MinBy" or "MaxBy";
    }

    private bool AnalyzeOutArgument(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IArgumentOperation argument,
        bool? whenValue,
        ITypeSymbol result)
    {
        var display = ResultTypes.Display(result);
        var name = invocation.TargetMethod.Name;

        // [MaybeNull] without a condition: the argument may always be default.
        if (whenValue is null)
        {
            DefaultResultAnalyzer.Report(context, Descriptors.MayReturnDefault, argument.Syntax, display, $"'{name}' may leave it default");
            return true;
        }

        IOperation current = invocation;
        var parent = DefaultUse.SkipConversions(invocation.Parent, ref current);
        if (parent is IExpressionStatementOperation
            || parent is ISimpleAssignmentOperation { Target: IDiscardOperation })
        {
            var reason = $"the result of '{name}' is ignored, and it leaves default when it returns {Bool(whenValue.Value)}";
            DefaultResultAnalyzer.Report(context, Descriptors.MayReturnDefault, argument.Syntax, display, reason);
            return true;
        }

        var negated = false;
        if (parent is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } not)
        {
            negated = true;
            current = not;
            parent = DefaultUse.SkipConversions(not.Parent, ref current);
        }

        if (parent is not IConditionalOperation conditional
            || conditional.Condition != current
            || OutLocal(argument.Value) is not { } local)
        {
            return false;
        }

        // The call leaves default when it returns whenValue; the condition is then whenValue, or its negation.
        var failingIsTrueBranch = negated ? !whenValue.Value : whenValue.Value;
        var failing = failingIsTrueBranch ? conditional.WhenTrue : conditional.WhenFalse;
        var why = $"'{name}' returned {Bool(whenValue.Value)} on this path";

        var reported = false;
        if (failing is not null && !Writes(failing, local))
        {
            reported |= ReportReads(context, failing, local, display, why);
        }

        var exitsOrAssigns = failing is not null && (FlowFacts.AlwaysExits(failing) || Writes(failing, local));
        if (!exitsOrAssigns && conditional.Parent is IBlockOperation block)
        {
            reported |= ReportReadsAfter(context, block, conditional, local, display, why);
        }

        return reported;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static ILocalSymbol? OutLocal(IOperation value)
    {
        return value switch
        {
            IDeclarationExpressionOperation { Expression: ILocalReferenceOperation declared } => declared.Local,
            ILocalReferenceOperation existing => existing.Local,
            _ => null,
        };
    }

    private static bool Writes(IOperation scope, ILocalSymbol local)
    {
        foreach (var operation in scope.DescendantsAndSelf())
        {
            if (operation is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local)
                && IsWrite(reference))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReportReads(OperationAnalysisContext context, IOperation scope, ILocalSymbol local, string display, string why)
    {
        var reported = false;
        foreach (var operation in scope.DescendantsAndSelf())
        {
            if (operation is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local)
                && !IsWrite(reference))
            {
                DefaultResultAnalyzer.Report(context, Descriptors.MayReturnDefault, reference.Syntax, display, why);
                reported = true;
            }
        }

        return reported;
    }

    private static bool ReportReadsAfter(
        OperationAnalysisContext context,
        IBlockOperation block,
        IOperation statement,
        ILocalSymbol local,
        string display,
        string why)
    {
        var reported = false;
        var after = false;
        foreach (var next in block.Operations)
        {
            if (!after)
            {
                after = next == statement;
                continue;
            }

            if (Writes(next, local))
            {
                break;
            }

            reported |= ReportReads(context, next, local, display, why);
        }

        return reported;
    }
}
