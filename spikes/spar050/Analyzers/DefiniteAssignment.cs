using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// A conservative "is this member assigned on every path through the constructor body?" check. It follows blocks,
/// if/else, switch with a default, try/catch, lock and using, and stops at the first return or goto that can skip the
/// assignment. Assignments inside loops, lambdas or called helpers do not count, as in nullable analysis without
/// [MemberNotNull].
/// </summary>
internal static class DefiniteAssignment
{
    public static bool Assigns(IOperation operation, Func<ISymbol, bool> isMember)
    {
        switch (operation)
        {
            case IBlockOperation block:
                foreach (var statement in block.Operations)
                {
                    if (Assigns(statement, isMember))
                    {
                        return true;
                    }

                    if (FlowFacts.MayLeave(statement, includeLoopBranches: false))
                    {
                        return false;
                    }
                }

                return false;

            case IExpressionStatementOperation statement:
                return AssignsInExpression(statement.Operation, isMember);

            case IConditionalOperation conditional:
                return conditional.WhenFalse is not null
                    && Assigns(conditional.WhenTrue, isMember)
                    && Assigns(conditional.WhenFalse, isMember);

            case ISwitchOperation @switch:
                return SwitchAssigns(@switch, isMember);

            case ITryOperation @try:
                return TryAssigns(@try, isMember);

            case ILockOperation @lock:
                return Assigns(@lock.Body, isMember);

            case IUsingOperation @using:
                return Assigns(@using.Body, isMember);

            case IThrowOperation:
                return true;

            default:
                return false;
        }
    }

    private static bool AssignsInExpression(IOperation operation, Func<ISymbol, bool> isMember)
    {
        switch (operation)
        {
            case ISimpleAssignmentOperation assignment:
                return IsOwnMember(assignment.Target, isMember)
                    || assignment.Target is IInstanceReferenceOperation
                    || AssignsInExpression(assignment.Value, isMember);

            case IDeconstructionAssignmentOperation { Target: ITupleOperation tuple }:
                foreach (var element in tuple.Elements)
                {
                    if (IsOwnMember(element, isMember))
                    {
                        return true;
                    }
                }

                return false;

            case IThrowOperation:
                return true;

            case IInvocationOperation invocation:
                // Passing the member as an out argument assigns it when the call returns.
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter?.RefKind == RefKind.Out && IsOwnMember(argument.Value, isMember))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private static bool IsOwnMember(IOperation target, Func<ISymbol, bool> isMember)
    {
        return target is IMemberReferenceOperation reference
            && (reference.Instance is null or IInstanceReferenceOperation)
            && isMember(reference.Member);
    }

    private static bool SwitchAssigns(ISwitchOperation @switch, Func<ISymbol, bool> isMember)
    {
        var hasDefault = false;
        foreach (var section in @switch.Cases)
        {
            foreach (var clause in section.Clauses)
            {
                hasDefault |= clause.CaseKind == CaseKind.Default;
            }

            var assigned = false;
            foreach (var statement in section.Body)
            {
                if (Assigns(statement, isMember))
                {
                    assigned = true;
                    break;
                }

                if (FlowFacts.MayLeave(statement, includeLoopBranches: false))
                {
                    return false;
                }
            }

            if (!assigned)
            {
                return false;
            }
        }

        return hasDefault;
    }

    private static bool TryAssigns(ITryOperation @try, Func<ISymbol, bool> isMember)
    {
        if (@try.Finally is not null && Assigns(@try.Finally, isMember))
        {
            return true;
        }

        if (!Assigns(@try.Body, isMember))
        {
            return false;
        }

        foreach (var handler in @try.Catches)
        {
            if (!Assigns(handler.Handler, isMember) && !FlowFacts.AlwaysExits(handler.Handler))
            {
                return false;
            }
        }

        return true;
    }
}
