using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// Recognises the one array-filling shape that provably writes every element before anyone can read the array:
/// <code>
/// var results = new Result&lt;…&gt;[n];
/// for (var i = 0; i &lt; results.Length; i++)   // or i &lt; n
/// {
///     …statements that neither leave the loop nor touch results…
///     results[i] = …;                             // not reading results
/// }
/// </code>
/// If the loop throws, the array is a local of the block being left, so its default elements are never seen.
/// </summary>
internal static class FillLoop
{
    public static bool FillsEveryElement(IArrayCreationOperation creation)
    {
        if (creation.DimensionSizes.Length != 1
            || creation.Parent is not IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }
            || declarator.Parent?.Parent is not IVariableDeclarationGroupOperation group
            || group.Parent is not IBlockOperation block)
        {
            return false;
        }

        var index = block.Operations.IndexOf(group);
        if (index < 0 || index + 1 >= block.Operations.Length || block.Operations[index + 1] is not IForLoopOperation loop)
        {
            return false;
        }

        var array = declarator.Symbol;
        var counter = CounterFromZero(loop);
        if (counter is null
            || !BoundIsLength(loop, counter, array, creation.DimensionSizes[0])
            || !StepsByOne(loop, counter)
            || FlowFacts.WritesLocal(loop.Body, counter))
        {
            return false;
        }

        var statements = loop.Body is IBlockOperation body ? body.Operations : ImmutableArray.Create(loop.Body);
        foreach (var statement in statements)
        {
            if (IsElementAssignment(statement, array, counter, out var value))
            {
                return !FlowFacts.References(value, array);
            }

            if (FlowFacts.MayLeave(statement, includeLoopBranches: true) || FlowFacts.References(statement, array))
            {
                return false;
            }
        }

        return false;
    }

    private static ILocalSymbol? CounterFromZero(IForLoopOperation loop)
    {
        if (loop.Before.Length != 1
            || loop.Before[0] is not IVariableDeclarationGroupOperation { Declarations.Length: 1 } group
            || group.Declarations[0].Declarators.Length != 1)
        {
            return null;
        }

        var declarator = group.Declarations[0].Declarators[0];
        var initial = declarator.Initializer?.Value ?? group.Declarations[0].Initializer?.Value;
        return initial?.ConstantValue is { HasValue: true, Value: 0 } ? declarator.Symbol : null;
    }

    private static bool BoundIsLength(IForLoopOperation loop, ILocalSymbol counter, ILocalSymbol array, IOperation size)
    {
        if (loop.Condition is not IBinaryOperation { OperatorKind: BinaryOperatorKind.LessThan } condition
            || FlowFacts.SkipConversions(condition.LeftOperand) is not ILocalReferenceOperation left
            || !SymbolEqualityComparer.Default.Equals(left.Local, counter))
        {
            return false;
        }

        var bound = FlowFacts.SkipConversions(condition.RightOperand);
        if (bound is IPropertyReferenceOperation { Property.Name: "Length", Instance: ILocalReferenceOperation instance })
        {
            return SymbolEqualityComparer.Default.Equals(instance.Local, array);
        }

        // i < n with the same n as the size: only a local or parameter the loop does not change.
        var sizeValue = FlowFacts.SkipConversions(size);
        return (bound, sizeValue) switch
        {
            (ILocalReferenceOperation a, ILocalReferenceOperation b) =>
                SymbolEqualityComparer.Default.Equals(a.Local, b.Local) && !FlowFacts.WritesLocal(loop.Body, a.Local),
            (IParameterReferenceOperation a, IParameterReferenceOperation b) =>
                SymbolEqualityComparer.Default.Equals(a.Parameter, b.Parameter) && !WritesParameter(loop.Body, a.Parameter),
            _ => false,
        };
    }

    private static bool StepsByOne(IForLoopOperation loop, ILocalSymbol counter)
    {
        if (loop.AtLoopBottom.Length != 1)
        {
            return false;
        }

        var step = loop.AtLoopBottom[0] is IExpressionStatementOperation statement ? statement.Operation : loop.AtLoopBottom[0];
        return step switch
        {
            IIncrementOrDecrementOperation { Kind: OperationKind.Increment, Target: ILocalReferenceOperation target } =>
                SymbolEqualityComparer.Default.Equals(target.Local, counter),
            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add, Target: ILocalReferenceOperation target } compound =>
                SymbolEqualityComparer.Default.Equals(target.Local, counter)
                && compound.Value.ConstantValue is { HasValue: true, Value: 1 },
            _ => false,
        };
    }

    private static bool IsElementAssignment(IOperation statement, ILocalSymbol array, ILocalSymbol counter, out IOperation value)
    {
        value = statement;
        if (statement is not IExpressionStatementOperation { Operation: ISimpleAssignmentOperation assignment }
            || assignment.Target is not IArrayElementReferenceOperation { Indices.Length: 1 } element
            || element.ArrayReference is not ILocalReferenceOperation arrayReference
            || !SymbolEqualityComparer.Default.Equals(arrayReference.Local, array)
            || FlowFacts.SkipConversions(element.Indices[0]) is not ILocalReferenceOperation indexReference
            || !SymbolEqualityComparer.Default.Equals(indexReference.Local, counter))
        {
            return false;
        }

        value = assignment.Value;
        return true;
    }

    private static bool WritesParameter(IOperation scope, IParameterSymbol parameter)
    {
        foreach (var operation in scope.DescendantsAndSelf())
        {
            if (operation is IParameterReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter)
                && MaybeDefaultCalls.IsWrite(reference))
            {
                return true;
            }
        }

        return false;
    }
}
