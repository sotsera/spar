using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>Small, conservative control-flow questions over operation trees.</summary>
internal static class FlowFacts
{
    /// <summary>True when the statement never completes normally (it returns, throws, breaks, continues or jumps).</summary>
    public static bool AlwaysExits(IOperation operation)
    {
        switch (operation)
        {
            case IBlockOperation block:
                return block.Operations.Length > 0 && AlwaysExits(block.Operations[block.Operations.Length - 1]);
            case IReturnOperation returned:
                return returned.Kind != OperationKind.YieldReturn;
            case IThrowOperation:
            case IExpressionStatementOperation { Operation: IThrowOperation }:
            case IBranchOperation:
                return true;
            case IConditionalOperation conditional:
                return conditional.WhenFalse is not null
                    && AlwaysExits(conditional.WhenTrue)
                    && AlwaysExits(conditional.WhenFalse);
            default:
                return false;
        }
    }

    /// <summary>True when the statement contains a return or goto of its own (not inside a lambda or local function).</summary>
    public static bool MayLeave(IOperation operation, bool includeLoopBranches)
    {
        var pending = new Stack<IOperation>();
        pending.Push(operation);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            switch (current)
            {
                case IAnonymousFunctionOperation:
                case ILocalFunctionOperation:
                    continue;
                case IReturnOperation { Kind: OperationKind.Return or OperationKind.YieldBreak }:
                case IBranchOperation { BranchKind: BranchKind.GoTo }:
                    return true;
                case IBranchOperation when includeLoopBranches:
                    return true;
            }

            foreach (var child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return false;
    }

    public static bool References(IOperation scope, ILocalSymbol local)
    {
        foreach (var operation in scope.DescendantsAndSelf())
        {
            if (operation is ILocalReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                return true;
            }
        }

        return false;
    }

    public static bool WritesLocal(IOperation scope, ILocalSymbol local)
    {
        foreach (var operation in scope.DescendantsAndSelf())
        {
            if (operation is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local)
                && (MaybeDefaultCalls.IsWrite(reference) || reference.Parent is IIncrementOrDecrementOperation))
            {
                return true;
            }
        }

        return false;
    }

    public static IOperation SkipConversions(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}
