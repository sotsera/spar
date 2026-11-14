using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Spar.Analyzers;

/// <summary>
/// .NET APIs known to create default values of their T without saying so in their signature: storage that starts
/// zeroed (SPAR052), factories that bypass constructors (SPAR050) and reinterpreting casts (SPAR051).
/// </summary>
internal static class KnownApis
{
    // Every invocation is checked, so filter on the method name before building the type's metadata name.
    private static readonly HashSet<string> s_methodNames =
    [
        "Resize", "AllocateUninitializedArray", "AllocateArray", "SetCount", "GetValueRefOrAddDefault", "Clear", "Rent",
        "CreateInstance", "CreateInstanceFromArrayType", "GetUninitializedObject", "SkipInit", "As", "BitCast", "Read",
        "ReadUnaligned", "GetValueOrDefault", "DefaultIfEmpty",
    ];

    private static readonly HashSet<string> s_holderNames = ["Lazy", "ThreadLocal", "AsyncLocal", "StrongBox"];

    /// <summary>Returns true when the call matched a known API (reported or not), so other rules skip it.</summary>
    public static bool AnalyzeInvocation(OperationAnalysisContext context, IInvocationOperation invocation, ResultTypes types)
    {
        var method = invocation.TargetMethod;
        if (!s_methodNames.Contains(method.Name))
        {
            return false;
        }

        var typeName = ResultTypes.FullMetadataName(method.ContainingType.OriginalDefinition);

        switch (typeName, method.Name)
        {
            case ("System.Array", "Resize"):
            case ("System.GC", "AllocateUninitializedArray"):
            case ("System.GC", "AllocateArray"):
            case ("System.Runtime.InteropServices.CollectionsMarshal", "SetCount"):
                return ReportElements(context, invocation, types, TypeArgument(method, 0));

            case ("System.Runtime.InteropServices.CollectionsMarshal", "GetValueRefOrAddDefault"):
                return ReportElements(context, invocation, types, TypeArgument(method, 1));

            case ("System.Span`1", "Clear"):
            case ("System.Buffers.ArrayPool`1", "Rent"):
            case ("System.Buffers.MemoryPool`1", "Rent"):
                return ReportElements(context, invocation, types, method.ContainingType.TypeArguments[0]);

            case ("System.Array", "Clear"):
                return ReportElements(context, invocation, types, ArrayElementOfArgument(invocation, 0));

            case ("System.Array", "CreateInstance"):
                return ReportElements(context, invocation, types, TypeOfArgument(invocation, 0));

            case ("System.Array", "CreateInstanceFromArrayType"):
                return ReportElements(context, invocation, types, (TypeOfArgument(invocation, 0) as IArrayTypeSymbol)?.ElementType);

            case ("System.Activator", "CreateInstance"):
                return ReportCreated(context, invocation, types, method.IsGenericMethod ? TypeArgument(method, 0) : TypeOfArgument(invocation, 0));

            case ("System.Runtime.CompilerServices.RuntimeHelpers", "GetUninitializedObject"):
                return ReportCreated(context, invocation, types, TypeOfArgument(invocation, 0));

            case ("System.Runtime.CompilerServices.Unsafe", "SkipInit"):
                return ReportCreated(context, invocation, types, TypeArgument(method, 0));

            case ("System.Runtime.CompilerServices.Unsafe", "As") when method.TypeArguments.Length == 2:
            case ("System.Runtime.CompilerServices.Unsafe", "BitCast"):
                return ReportMaybe(context, invocation, types, TypeArgument(method, 1), "it reinterprets memory as a Result");

            case ("System.Runtime.CompilerServices.Unsafe", "Read"):
            case ("System.Runtime.CompilerServices.Unsafe", "ReadUnaligned"):
                return ReportMaybe(context, invocation, types, TypeArgument(method, 0), "it reinterprets memory as a Result");

            case ("System.Nullable`1", "GetValueOrDefault") when method.Parameters.Length == 0:
                return ReportMaybe(context, invocation, types, method.ContainingType.TypeArguments[0], "when it has no value");

            case ("System.Linq.Enumerable", "DefaultIfEmpty") when method.Parameters.Length == 1:
                return ReportMaybe(context, invocation, types, TypeArgument(method, 0), "when the sequence is empty");

            default:
                return false;
        }
    }

    /// <summary>Constructors of BCL holders whose value starts as default(T) when no factory or value is given.</summary>
    public static bool AnalyzeCreation(OperationAnalysisContext context, IObjectCreationOperation creation, ResultTypes types)
    {
        if (creation.Type is not INamedTypeSymbol { IsGenericType: true } type
            || creation.Constructor is not { } constructor
            || !s_holderNames.Contains(type.Name))
        {
            return false;
        }

        var valueType = type.TypeArguments[0];
        switch (ResultTypes.FullMetadataName(type.OriginalDefinition))
        {
            case "System.Lazy`1" when !HasFactory(constructor):
                // Lazy<T> without a factory creates its value with Activator.CreateInstance<T>().
                if (types.HoldsResult(valueType))
                {
                    var description = $"a Lazy whose Value is {types.DescribeDefault(valueType)}";
                    DefaultResultAnalyzer.Report(context, Descriptors.DefaultResult, creation.Syntax, description);
                }

                return true;

            case "System.Threading.ThreadLocal`1" when !HasFactory(constructor):
                return ReportMaybe(context, creation, types, valueType, "its Value is default on each thread until set");

            case "System.Threading.AsyncLocal`1":
            case "System.Runtime.CompilerServices.StrongBox`1" when constructor.Parameters.Length == 0:
                return ReportMaybe(context, creation, types, valueType, "its Value is default until set");

            default:
                return false;
        }
    }

    /// <summary>ImmutableArray&lt;T&gt;.Builder.Count = n grows the builder with default elements.</summary>
    public static void AnalyzePropertyWrite(OperationAnalysisContext context, ResultTypes types)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (reference.Property.Name != "Count"
            || reference.Parent is not ISimpleAssignmentOperation assignment
            || assignment.Target != reference)
        {
            return;
        }

        var containing = reference.Property.ContainingType;
        if (ResultTypes.FullMetadataName(containing.OriginalDefinition) != "System.Collections.Immutable.ImmutableArray`1+Builder")
        {
            return;
        }

        var element = containing.ContainingType.TypeArguments[0];
        if (types.HoldsResult(element))
        {
            DefaultResultAnalyzer.Report(context, Descriptors.DefaultElements, assignment.Syntax, ResultTypes.Display(element));
        }
    }

    private static bool HasFactory(IMethodSymbol constructor)
    {
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.Type is INamedTypeSymbol { Name: "Func" } || parameter.Name == "value")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReportElements(OperationAnalysisContext context, IOperation operation, ResultTypes types, ITypeSymbol? element)
    {
        if (element is not null && types.HoldsResult(element))
        {
            DefaultResultAnalyzer.Report(context, Descriptors.DefaultElements, operation.Syntax, ResultTypes.Display(element));
        }

        return true;
    }

    private static bool ReportCreated(OperationAnalysisContext context, IOperation operation, ResultTypes types, ITypeSymbol? created)
    {
        if (created is not null && types.HoldsResult(created))
        {
            DefaultResultAnalyzer.Report(context, Descriptors.DefaultResult, operation.Syntax, types.DescribeDefault(created));
        }

        return true;
    }

    private static bool ReportMaybe(OperationAnalysisContext context, IOperation operation, ResultTypes types, ITypeSymbol? value, string reason)
    {
        var result = types.FindResult(value, 0);
        if (result is not null)
        {
            var display = ResultTypes.Display(result);
            DefaultResultAnalyzer.Report(context, Descriptors.MayReturnDefault, operation.Syntax, display, reason);
        }

        return true;
    }

    private static ITypeSymbol? TypeArgument(IMethodSymbol method, int index) =>
        index < method.TypeArguments.Length ? method.TypeArguments[index] : null;

    private static ITypeSymbol? TypeOfArgument(IInvocationOperation invocation, int index)
    {
        if (index >= invocation.Arguments.Length)
        {
            return null;
        }

        return invocation.Arguments[index].Value is ITypeOfOperation typeOf ? typeOf.TypeOperand : null;
    }

    private static ITypeSymbol? ArrayElementOfArgument(IInvocationOperation invocation, int index)
    {
        if (index >= invocation.Arguments.Length)
        {
            return null;
        }

        var value = invocation.Arguments[index].Value;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return (value.Type as IArrayTypeSymbol)?.ElementType;
    }
}
