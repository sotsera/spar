using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// Reads one contract into a <see cref="ContractModel"/>. The analyzer passes a report callback and shows every
/// problem at its location; the generator passes none and simply leaves out what is invalid. An invalid command never
/// drops its whole contract (decision "D1 follow-up: IDE design-time behaviour").
/// </summary>
internal sealed class ContractAnalysis
{
    private static readonly SymbolDisplayFormat CSharpFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly HashSet<string> ReservedPayloadNames = new(StringComparer.Ordinal) { "kind", "code", "message", "execution" };

    private readonly Action<Diagnostic>? _report;
    private readonly Dictionary<string, TypeRef> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordModel> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ErrorModel> _errors = new(StringComparer.Ordinal);

    private ContractAnalysis(Action<Diagnostic>? report)
    {
        _report = report;
    }

    public static ContractModel? Analyze(INamedTypeSymbol type, Action<Diagnostic>? report = null) =>
        new ContractAnalysis(report).Contract(type);

    private ContractModel? Contract(INamedTypeSymbol type)
    {
        var isInterface = type.TypeKind == TypeKind.Interface;
        var isSealedClass = type.TypeKind == TypeKind.Class && type.IsSealed && !type.IsStatic && !type.IsRecord;
        if (!isInterface && !isSealedClass)
        {
            Report(Descriptors.NotSealedClassOrInterface, type, type.Name);
            return null;
        }

        if (type.IsGenericType)
        {
            Report(Descriptors.GenericContract, type, type.Name);
            return null;
        }

        var wireName = ExplicitName(type, "SparContractAttribute") ?? Naming.Contract(type.Name, isInterface);
        var commands = new List<CommandModel>();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => IsCommand(m, isInterface)))
        {
            var command = Command(method);
            if (command is not null)
            {
                commands.Add(command);
            }
        }

        return new ContractModel(
            type.ToDisplayString(CSharpFormat),
            type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            wireName,
            Naming.Pascal(wireName),
            isInterface,
            commands.ToEquatable(),
            _types.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToEquatable(),
            _records.Values.OrderBy(r => r.Key, StringComparer.Ordinal).ToEquatable(),
            _errors.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToEquatable());
    }

    private static bool IsCommand(IMethodSymbol method, bool isInterface)
    {
        if (method.MethodKind != MethodKind.Ordinary || method.IsStatic)
        {
            return false;
        }

        return isInterface || HasAttribute(method, "SparCommandAttribute");
    }

    private CommandModel? Command(IMethodSymbol method)
    {
        if (method.IsGenericMethod)
        {
            Report(Descriptors.GenericCommand, method, method.Name);
            return null;
        }

        var parameters = method.Parameters;
        var takesToken = parameters.Length > 0 && IsCancellationToken(parameters[parameters.Length - 1].Type);
        var requests = takesToken ? parameters.Take(parameters.Length - 1).ToList() : parameters.ToList();
        foreach (var parameter in parameters)
        {
            if (parameter.RefKind != RefKind.None || parameter.IsParams)
            {
                Report(Descriptors.RefParameter, parameter, parameter.Name, method.Name);
                return null;
            }

            if (IsCancellationToken(parameter.Type) && !SymbolEqualityComparer.Default.Equals(parameter, parameters[parameters.Length - 1]))
            {
                Report(Descriptors.TokenNotLast, parameter, method.Name);
                return null;
            }
        }

        if (requests.Count > 1)
        {
            Report(Descriptors.TooManyRequests, requests[1], method.Name);
            return null;
        }

        TypeRef? request = null;
        if (requests.Count == 1)
        {
            request = Wire(requests[0].Type, requests[0]);
            if (request is null)
            {
                return null;
            }
        }

        var shape = Return(method);
        if (shape is null)
        {
            return null;
        }

        var wireName = ExplicitName(method, "SparCommandAttribute") ?? Naming.Command(method.Name);
        return new CommandModel(
            method.Name, wireName, request, takesToken, shape.Value.Shape, shape.Value.Value, shape.Value.ErrorKey, shape.Value.ErrorCSharp, TimeoutSeconds(method));
    }

    private (ReturnShape Shape, TypeRef? Value, string? ErrorKey, string? ErrorCSharp)? Return(IMethodSymbol method)
    {
        if (method.ReturnType is not INamedTypeSymbol returned || !IsTaskLike(returned, out var inner))
        {
            Report(Descriptors.UnsupportedReturn, method, method.Name, method.ReturnType.ToDisplayString());
            return null;
        }

        if (inner is null)
        {
            return (ReturnShape.None, null, null, null);
        }

        if (inner is INamedTypeSymbol result && IsSparResult(result))
        {
            var errorType = (INamedTypeSymbol)result.TypeArguments[result.TypeArguments.Length - 1];
            var error = Error(errorType, method);
            if (error is null)
            {
                return null;
            }

            if (result.TypeArguments.Length == 1)
            {
                return (ReturnShape.ResultWithoutValue, null, error.Key, error.CSharp);
            }

            var resultValue = Wire(result.TypeArguments[0], method);
            return resultValue is null ? null : (ReturnShape.ResultWithValue, resultValue, error.Key, error.CSharp);
        }

        var value = Wire(inner, method);
        return value is null ? null : (ReturnShape.Value, value, null, null);
    }

    /// <summary>Maps a C# type to a wire type, collecting the records it needs, or reports SPAR020.</summary>
    private TypeRef? Wire(ITypeSymbol type, ISymbol location)
    {
        var csharp = type.ToDisplayString(CSharpFormat);
        if (_types.TryGetValue(csharp, out var known))
        {
            return known;
        }

        var mapped = Map(type, location);
        if (mapped is not null)
        {
            _types[csharp] = mapped;
        }

        return mapped;
    }

    private TypeRef? Map(ITypeSymbol type, ISymbol location)
    {
        var csharp = type.ToDisplayString(CSharpFormat);
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableValue)
        {
            return Nullable(nullableValue.TypeArguments[0], location, csharp, isValueType: true);
        }

        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return Nullable(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), location, csharp, isValueType: false);
        }

        var primitive = Primitive(type);
        if (primitive is not null)
        {
            return new TypeRef(primitive.Value, csharp, primitive.Value.ToString(), null, type.IsValueType);
        }

        if (List(type) is { } element)
        {
            var item = Wire(element, location);
            return item is null ? null : new TypeRef(WireKind.List, csharp, "ListOf" + item.Key, item, false);
        }

        if (type is INamedTypeSymbol named && named.IsRecord && named.TypeKind == TypeKind.Class)
        {
            return Record(named, location);
        }

        Report(Descriptors.UnsupportedWireType, location, type.ToDisplayString());
        return null;
    }

    private TypeRef? Nullable(ITypeSymbol inner, ISymbol location, string csharp, bool isValueType)
    {
        var value = Wire(inner, location);
        return value is null ? null : new TypeRef(WireKind.Nullable, csharp, "Nullable" + value.Key, value, isValueType);
    }

    private TypeRef? Record(INamedTypeSymbol record, ISymbol location)
    {
        var csharp = record.ToDisplayString(CSharpFormat);
        var key = "Record_" + Naming.Identifier(csharp);
        var reference = new TypeRef(WireKind.Record, csharp, key, null, false);
        if (_records.ContainsKey(csharp))
        {
            return reference;
        }

        if (record.IsAbstract || record.IsGenericType)
        {
            Report(Descriptors.NotPositionalRecord, record, record.Name);
            return null;
        }

        // Registered before its properties, so a record that refers to itself does not recurse forever.
        _types[csharp] = reference;
        var properties = Properties(record, location);
        if (properties is null)
        {
            _types.Remove(csharp);
            return null;
        }

        _records[csharp] = new RecordModel(csharp, key, properties.ToEquatable());
        return reference;
    }

    /// <summary>The values of a positional record: its primary constructor's parameters, in order.</summary>
    private List<PropertyModel>? Properties(INamedTypeSymbol record, ISymbol location)
    {
        var constructor = PrimaryConstructor(record);
        if (constructor is null)
        {
            Report(Descriptors.NotPositionalRecord, record, record.Name);
            return null;
        }

        var properties = new List<PropertyModel>();
        foreach (var parameter in constructor.Parameters)
        {
            var type = Wire(parameter.Type, parameter);
            if (type is null)
            {
                return null;
            }

            properties.Add(new PropertyModel(parameter.Name, Naming.CamelCase(parameter.Name), type));
        }

        return properties;
    }

    private static IMethodSymbol? PrimaryConstructor(INamedTypeSymbol record)
    {
        // A record with no parameter list has only its implicit and copy constructors: no values.
        var candidates = record.InstanceConstructors
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, record)))
            .ToList();
        var withProperties = candidates.FirstOrDefault(c => c.Parameters.Length > 0
            && c.Parameters.All(p => record.GetMembers(p.Name).OfType<IPropertySymbol>().Any()));
        return withProperties ?? candidates.FirstOrDefault(c => c.Parameters.Length == 0);
    }

    private ErrorModel? Error(INamedTypeSymbol type, ISymbol location)
    {
        var csharp = type.ToDisplayString(CSharpFormat);
        if (_errors.TryGetValue(csharp, out var known))
        {
            return known;
        }

        if (!type.IsRecord || type.IsGenericType)
        {
            Report(Descriptors.BadErrorType, location, type.Name);
            return null;
        }

        var cases = new List<ErrorCaseModel>();
        foreach (var nested in type.GetTypeMembers().Where(t => SymbolEqualityComparer.Default.Equals(t.BaseType, type)))
        {
            if (!nested.IsRecord || !nested.IsSealed || nested.IsGenericType)
            {
                Report(Descriptors.BadErrorType, nested, type.Name);
                return null;
            }

            var properties = Properties(nested, nested);
            if (properties is null)
            {
                return null;
            }

            var reserved = properties.FirstOrDefault(p => ReservedPayloadNames.Contains(p.JsonName));
            if (reserved is not null)
            {
                Report(Descriptors.ReservedPayloadName, nested, nested.Name, reserved.JsonName);
                return null;
            }

            var code = ExplicitName(nested, "SparNameAttribute") ?? Naming.CamelCase(nested.Name);
            cases.Add(new ErrorCaseModel(nested.ToDisplayString(CSharpFormat), code, properties.ToEquatable()));
        }

        if (cases.Count == 0)
        {
            Report(Descriptors.NoErrorCases, location, type.Name);
            return null;
        }

        var error = new ErrorModel(csharp, "Error_" + Naming.Identifier(csharp), cases.ToEquatable());
        _errors[csharp] = error;
        return error;
    }

    private static WireKind? Primitive(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return WireKind.String;
            case SpecialType.System_Boolean:
                return WireKind.Boolean;
            case SpecialType.System_Int32:
                return WireKind.Int32;
            case SpecialType.System_Int64:
                return WireKind.Int64;
            case SpecialType.System_Double:
                return WireKind.Double;
        }

        var name = type.ToDisplayString();
        return name switch
        {
            "System.Guid" => WireKind.Guid,
            "System.DateTimeOffset" => WireKind.DateTimeOffset,
            _ => null,
        };
    }

    private static ITypeSymbol? List(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            return array.ElementType;
        }

        return type is INamedTypeSymbol { IsGenericType: true } named
            && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>"
            ? named.TypeArguments[0]
            : null;
    }

    private static bool IsTaskLike(INamedTypeSymbol type, out ITypeSymbol? inner)
    {
        inner = null;
        var definition = type.OriginalDefinition.ToDisplayString();
        switch (definition)
        {
            case "System.Threading.Tasks.Task":
            case "System.Threading.Tasks.ValueTask":
                return true;
            case "System.Threading.Tasks.Task<TResult>":
            case "System.Threading.Tasks.ValueTask<TResult>":
                inner = type.TypeArguments[0];
                return true;
            default:
                return false;
        }
    }

    private static bool IsSparResult(INamedTypeSymbol type) =>
        type.Name == "Result" && type.ContainingNamespace.ToDisplayString() == "Spar" && type.TypeArguments.Length is 1 or 2;

    private static bool IsCancellationToken(ITypeSymbol type) => type.ToDisplayString() == "System.Threading.CancellationToken";

    private static bool HasAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.Name == name && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Spar");

    private static string? ExplicitName(ISymbol symbol, string attribute)
    {
        var data = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == attribute);
        return data is { ConstructorArguments.Length: > 0 } && data.ConstructorArguments[0].Value is string { Length: > 0 } name ? name : null;
    }

    private static int TimeoutSeconds(IMethodSymbol method)
    {
        var data = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "SparCommandAttribute");
        var argument = data?.NamedArguments.FirstOrDefault(a => a.Key == "TimeoutSeconds");
        return argument?.Value.Value is int seconds ? seconds : 0;
    }

    private void Report(DiagnosticDescriptor descriptor, ISymbol symbol, params object[] arguments)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
        _report?.Invoke(Diagnostic.Create(descriptor, location, arguments));
    }
}
