using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>Uses of a default value that do not create a Result anyone reads as a value or an error.</summary>
internal static class DefaultUse
{
    /// <summary>
    /// True when the default is compared against (an Equals argument), or stored where the API declares that a
    /// default may appear: an out parameter marked [MaybeNull]/[MaybeNullWhen], or the return of a method marked
    /// [return: MaybeNull]. Callers of such members get SPAR051 instead.
    /// </summary>
    public static bool IsDeclaredOrCompared(IOperation operation, ISymbol containingSymbol)
    {
        var current = operation;
        var parent = SkipValueFlow(operation.Parent, ref current);

        if (parent is IArgumentOperation { Parent: IInvocationOperation { TargetMethod.Name: "Equals" } })
        {
            return true;
        }

        if (parent is ISimpleAssignmentOperation assignment
            && assignment.Value == current
            && assignment.Target is IParameterReferenceOperation { Parameter: { RefKind: RefKind.Out } parameter }
            && ResultTypes.MayLeaveDefault(parameter, out _))
        {
            return true;
        }

        if (parent is IReturnOperation returned)
        {
            var function = EnclosingFunction(returned, containingSymbol);
            return function is not null && ReturnsMaybeNull(function);
        }

        return false;
    }

    public static bool ReturnsMaybeNull(IMethodSymbol method)
    {
        if (ResultTypes.HasCodeAnalysisAttribute(method.GetReturnTypeAttributes(), "MaybeNullAttribute"))
        {
            return true;
        }

        return method.AssociatedSymbol is IPropertySymbol property
            && ResultTypes.HasCodeAnalysisAttribute(property.GetAttributes(), "MaybeNullAttribute");
    }

    public static IOperation? SkipConversions(IOperation? parent, ref IOperation current)
    {
        while (parent is IConversionOperation { IsImplicit: true } or IParenthesizedOperation)
        {
            current = parent;
            parent = parent.Parent;
        }

        return parent;
    }

    /// <summary>
    /// Like <see cref="SkipConversions"/>, and also through the operations that pass a value on unchanged: a branch of
    /// ?:, the fallback of ??, and a switch expression arm. So <c>ok ? x : default</c> is judged by where it goes.
    /// </summary>
    public static IOperation? SkipValueFlow(IOperation? parent, ref IOperation current)
    {
        while (true)
        {
            parent = SkipConversions(parent, ref current);
            var passesValue = parent switch
            {
                IConditionalOperation conditional => conditional.Condition != current,
                ICoalesceOperation coalesce => coalesce.WhenNull == current,
                ISwitchExpressionArmOperation arm => arm.Value == current,
                ISwitchExpressionOperation => true,
                _ => false,
            };

            if (!passesValue)
            {
                return parent;
            }

            current = parent!;
            parent = parent!.Parent;
        }
    }

    /// <summary>The lambda, local function or member a return statement belongs to.</summary>
    private static IMethodSymbol? EnclosingFunction(IOperation operation, ISymbol containingSymbol)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IAnonymousFunctionOperation lambda:
                    return lambda.Symbol;
                case ILocalFunctionOperation local:
                    return local.Symbol;
            }
        }

        return containingSymbol as IMethodSymbol;
    }
}
