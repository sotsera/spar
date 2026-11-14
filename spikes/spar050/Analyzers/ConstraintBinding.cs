using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// A Result bound to a type parameter constrained with new(). Every struct satisfies new(), and new T() on a Result
/// is default(Result), so the binding is reported where it is written: a generic call, or the creation of a generic
/// type. This works for metadata methods too, because it needs only the signature.
/// </summary>
internal static class ConstraintBinding
{
    public static bool Report(OperationAnalysisContext context, IInvocationOperation invocation, ResultTypes types)
    {
        var method = invocation.TargetMethod;
        if (!method.IsGenericMethod)
        {
            return false;
        }

        return Report(context, invocation.Syntax, method.OriginalDefinition.TypeParameters, method.TypeArguments, types);
    }

    public static bool Report(OperationAnalysisContext context, IObjectCreationOperation creation, ResultTypes types)
    {
        if (creation.Type is not INamedTypeSymbol { IsGenericType: true } type)
        {
            return false;
        }

        return Report(context, creation.Syntax, type.OriginalDefinition.TypeParameters, type.TypeArguments, types);
    }

    private static bool Report(
        OperationAnalysisContext context,
        SyntaxNode syntax,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments,
        ResultTypes types)
    {
        for (var i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            if (parameters[i].HasConstructorConstraint && types.HoldsResult(arguments[i]))
            {
                var description = $"{types.DescribeDefault(arguments[i])} through new {parameters[i].Name}()";
                DefaultResultAnalyzer.Report(context, Descriptors.DefaultResult, syntax, description);
                return true;
            }
        }

        return false;
    }
}
