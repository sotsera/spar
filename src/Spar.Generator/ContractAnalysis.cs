using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>
/// Reads one contract into a <see cref="ContractModel"/>. The analyzer passes a report callback and shows every
/// problem at its location; the generator and the client step pass none and leave out what is invalid. An invalid
/// command never drops its whole contract (decision "D1 follow-up: IDE design-time behaviour"); a problem with the
/// contract's own name, or two of its types with one TypeScript name, does.
/// </summary>
internal sealed partial class ContractAnalysis
{
    private const string LowerRule = "a camelCase ASCII name (a lower-case letter, then letters and digits)";

    private static readonly SymbolDisplayFormat CSharpFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly HashSet<string> ReservedPayloadNames = new(StringComparer.Ordinal) { "kind", "code", "message", "execution" };

    private readonly Action<ISymbol, Diagnostic>? _report;
    private readonly IDictionary<string, INamedTypeSymbol>? _symbols;
    private readonly Dictionary<string, TypeRef> _types = new(StringComparer.Ordinal);

    /// <summary>The places already warned about DateTime: a list of them is warned about once, not again for its items.</summary>
    private readonly HashSet<ISymbol> _warnedDateTime = new(SymbolEqualityComparer.Default);

    private readonly Dictionary<string, RecordModel> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ErrorModel> _errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumModel> _enums = new(StringComparer.Ordinal);

    /// <summary>The contract and every record and error type it uses, by C# name, to report clashes at each.</summary>
    private readonly Dictionary<string, INamedTypeSymbol> _named = new(StringComparer.Ordinal);

    private ContractAnalysis(Action<ISymbol, Diagnostic>? report, IDictionary<string, INamedTypeSymbol>? symbols)
    {
        _report = report;
        _symbols = symbols;
    }

    public static ContractModel? Analyze(INamedTypeSymbol type) => new ContractAnalysis(null, null).Contract(type);

    /// <summary>
    /// Reads a contract, reporting each problem with the symbol it is about; <paramref name="symbols"/>, when given,
    /// collects the contract and every record and error type it uses, by their C# names.
    /// </summary>
    public static ContractModel? Analyze(INamedTypeSymbol type, Action<ISymbol, Diagnostic> report, IDictionary<string, INamedTypeSymbol>? symbols = null) =>
        new ContractAnalysis(report, symbols).Contract(type);

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

        var explicitName = ExplicitName(type, "SparContractAttribute", out var attribute);
        var wireName = explicitName ?? Naming.Contract(type.Name, isInterface);
        var subject = $"Contract '{type.Name}'";
        var fix = explicitName is null ? "Give it a name with [SparContract(\"…\")]" : "Use another name in [SparContract(\"…\")]";
        if (!ValidContractName(type, attribute, subject, wireName, fix))
        {
            return null;
        }

        var commands = Commands(type, isInterface, wireName);
        var csharp = type.ToDisplayString(CSharpFormat);
        Remember(csharp, type);
        if (!ModuleNamesFree(type, attribute, subject, wireName, commands, fix) || !TypeNamesUnique(type, wireName))
        {
            return null;
        }

        return new ContractModel(
            csharp,
            type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            wireName,
            Naming.Pascal(wireName),
            isInterface,
            commands.ToEquatable(),
            _types.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToEquatable(),
            _records.Values.OrderBy(r => r.Key, StringComparer.Ordinal).ToEquatable(),
            _errors.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToEquatable(),
            _enums.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToEquatable());
    }

    private bool ValidContractName(INamedTypeSymbol type, AttributeData? attribute, string subject, string name, string fix)
    {
        if (!ClientNames.IsValidLowerName(name))
        {
            Report(Descriptors.InvalidName, type, attribute, subject, name, LowerRule, fix);
            return false;
        }

        var reason = ClientNames.ReservedSubpaths.Contains(name) ? "which @spar/app uses for one of its own subpaths"
            : ClientNames.IsReserved(name) ? "a reserved word in JavaScript"
            : ClientNames.ModuleNames.Contains(name) ? "which its generated module uses itself"
            : null;
        if (reason is null)
        {
            return true;
        }

        Report(Descriptors.ReservedName, type, attribute, subject, name, reason, fix);
        return false;
    }

    /// <summary>The contract's object must not take the name of a reader, writer or decoder its own module declares.</summary>
    private bool ModuleNamesFree(INamedTypeSymbol type, AttributeData? attribute, string subject, string name, List<CommandModel> commands, string fix)
    {
        var functions = new HashSet<string>(StringComparer.Ordinal);
        functions.UnionWith(_records.Values.SelectMany(r => new[] { ClientNames.Reader(r.Name), ClientNames.Writer(r.Name) }));
        functions.UnionWith(_errors.Values.Select(e => ClientNames.Reader(e.Name)));
        functions.UnionWith(_enums.Values.SelectMany(e => new[] { ClientNames.Reader(e.Name), ClientNames.Writer(e.Name) }));
        functions.UnionWith(commands.Select(c => ClientNames.Decoder(c.WireName)));
        if (!functions.Contains(name))
        {
            return true;
        }

        Report(Descriptors.ReservedName, type, attribute, subject, name, "which its generated module uses itself", fix);
        return false;
    }

    /// <summary>
    /// Within one contract's module each TypeScript type name is used once (SPAR040): reported at each clashing type the
    /// contract's own assembly declares, else at the contract (plan section 2, "Diagnostics").
    /// </summary>
    private bool TypeNamesUnique(INamedTypeSymbol contract, string contractName)
    {
        var named = _records.Values.Select(r => (r.CSharp, r.Name)).Concat(_errors.Values.Select(e => (e.CSharp, e.Name)))
            .Concat(_enums.Values.Select(e => (e.CSharp, e.Name)));
        var unique = true;
        foreach (var clash in named.GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            unique = false;
            var spellings = string.Join(" and ", clash.Select(t => Display(t.CSharp)).OrderBy(s => s, StringComparer.Ordinal));
            var declared = clash.Select(t => _named[t.CSharp])
                .Where(s => SymbolEqualityComparer.Default.Equals(s.ContainingAssembly, contract.ContainingAssembly))
                .ToList();
            foreach (var symbol in declared.Count > 0 ? declared : [contract])
            {
                Report(Descriptors.DuplicateTypeName, symbol, $"Contract '{contractName}' uses", clash.Key, spellings);
            }
        }

        return unique;
    }

    private List<CommandModel> Commands(INamedTypeSymbol type, bool isInterface, string contractName)
    {
        var found = new List<(IMethodSymbol Method, CommandModel Model)>();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => IsCommand(m, isInterface)))
        {
            var command = Command(method);
            if (command is not null)
            {
                found.Add((method, command));
            }
        }

        // Two commands with one name, overloads included, are each reported and both left out.
        var commands = new List<CommandModel>();
        foreach (var group in found.GroupBy(c => c.Model.WireName, StringComparer.Ordinal))
        {
            if (group.Count() == 1)
            {
                commands.Add(group.Single().Model);
                continue;
            }

            var names = string.Join(", ", group.Select(c => c.Method.Name));
            foreach (var (method, _) in group)
            {
                Report(Descriptors.DuplicateCommand, method, names, contractName, group.Key);
            }
        }

        return commands;
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

        var explicitName = ExplicitName(method, "SparCommandAttribute", out var attribute);
        var wireName = explicitName ?? Naming.Command(method.Name);
        if (!ClientNames.IsValidLowerName(wireName))
        {
            var fix = explicitName is null ? "Give it a name with [SparCommand(\"…\")]" : "Use another name in [SparCommand(\"…\")]";
            Report(Descriptors.InvalidName, method, attribute, $"Command '{method.Name}'", wireName, LowerRule, fix);
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
            request = Request(method, requests[0]);
            if (request is null)
            {
                return null;
            }
        }

        var shape = Return(method);
        if (shape is null || !Subscription(method, shape.Value.Shape, out var subscription)
            || !SerialGroup(method, shape.Value.Shape, out var serialGroup)
            || !Resource(method, request, shape.Value.Shape, shape.Value.Value, out var isResource)
            || !BlobsInPlace(method, request, shape.Value.Shape, shape.Value.Value, shape.Value.ErrorKey))
        {
            return null;
        }

        return new CommandModel(
            method.Name,
            wireName,
            request,
            requests.Count == 1 ? requests[0].Name : null,
            takesToken,
            shape.Value.Shape,
            shape.Value.Value,
            shape.Value.ErrorKey,
            shape.Value.ErrorCSharp,
            TimeoutSeconds(method),
            subscription,
            serialGroup,
            isResource,
            !isResource && HoldsBlob(shape.Value.Value),
            HoldsGrant(request));
    }

    /// <summary>A request is one record or one scalar, nullable or not (SPAR015 for a list); <c>byte[]</c> is a scalar.</summary>
    private TypeRef? Request(IMethodSymbol method, IParameterSymbol parameter)
    {
        var type = parameter.Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (List(type) is not null && !IsByteArray(type))
        {
            Report(Descriptors.UnsupportedRequest, parameter, method.Name, parameter.Type.ToDisplayString());
            return null;
        }

        return Wire(parameter.Type, parameter);
    }

    private (ReturnShape Shape, TypeRef? Value, string? ErrorKey, string? ErrorCSharp)? Return(IMethodSymbol method)
    {
        if (method.ReturnType is INamedTypeSymbol stream && IsAsyncEnumerable(stream))
        {
            return StreamReturn(method, stream.TypeArguments[0]);
        }

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

        // Before the cache, so every place that uses DateTime gets its warning (the same place is reported once).
        if (MentionsDateTime(type) && _warnedDateTime.Add(location))
        {
            Report(Descriptors.DateTimeOnWire, location, type.ToDisplayString());
        }

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

        if (IsByteArray(type))
        {
            return new TypeRef(WireKind.Bytes, csharp, nameof(WireKind.Bytes), null, false);
        }

        if (IsSparBlob(type))
        {
            return new TypeRef(WireKind.Blob, csharp, nameof(WireKind.Blob), null, false);
        }

        if (GrantKind(type) is { } grant)
        {
            return new TypeRef(grant, csharp, grant.ToString(), null, false);
        }

        var primitive = Primitive(type);
        if (primitive is not null)
        {
            return new TypeRef(primitive.Value, csharp, primitive.Value.ToString(), null, type.IsValueType);
        }

        if (List(type) is { } element)
        {
            // Arrays and read-only lists read the same JSON, but are different C# types with their own methods.
            var item = Wire(element, location);
            var prefix = type is IArrayTypeSymbol ? "ArrayOf" : "ListOf";
            return item is null ? null : new TypeRef(WireKind.List, csharp, prefix + item.Key, item, false);
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return Enum(enumType);
        }

        if (type is INamedTypeSymbol named && named.IsRecord && named.TypeKind == TypeKind.Class)
        {
            return Record(named);
        }

        Report(Descriptors.UnsupportedWireType, location, type.ToDisplayString());
        return null;
    }

    private TypeRef? Nullable(ITypeSymbol inner, ISymbol location, string csharp, bool isValueType)
    {
        var value = Wire(inner, location);
        return value is null ? null : new TypeRef(WireKind.Nullable, csharp, "Nullable" + value.Key, value, isValueType);
    }

    private TypeRef? Record(INamedTypeSymbol record)
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
            Report(Descriptors.NotPositionalRecord, record, record.Name, Descriptors.NotPositional);
            return null;
        }

        var name = TypeName(record, $"Record '{record.Name}'");
        if (name is null)
        {
            return null;
        }

        // Registered before its properties, so a record that refers to itself does not recurse forever.
        _types[csharp] = reference;
        var properties = Properties(record);
        if (properties is null)
        {
            _types.Remove(csharp);
            return null;
        }

        Remember(csharp, record);
        _records[csharp] = new RecordModel(csharp, key, name, properties.ToEquatable());
        return reference;
    }

    /// <summary>The TypeScript name of a record or error type: <c>[SparName]</c> or its own (SPAR003, SPAR004).</summary>
    private string? TypeName(INamedTypeSymbol type, string subject)
    {
        var explicitName = ExplicitName(type, "SparNameAttribute", out var attribute);
        var name = explicitName ?? type.Name;
        var fix = explicitName is null ? "Give it a TypeScript name with [SparName(\"…\")]" : "Use another name in [SparName(\"…\")]";
        if (!ClientNames.IsValidTypeName(name))
        {
            Report(Descriptors.InvalidName, type, attribute, subject, name, "a TypeScript name of ASCII letters and digits that starts with a letter", fix);
            return null;
        }

        var reason = ClientNames.IsReserved(name) ? "a reserved word in TypeScript"
            : ClientNames.SdkImports.Contains(name) ? "the name of a Spar SDK type the generated modules import"
            : null;
        if (reason is not null)
        {
            Report(Descriptors.ReservedName, type, attribute, subject, name, reason, fix);
            return null;
        }

        return name;
    }

    /// <summary>The values of a positional record: its primary constructor's parameters, in order.</summary>
    private List<PropertyModel>? Properties(INamedTypeSymbol record)
    {
        var constructor = Constructor(record);
        if (constructor is null)
        {
            return null;
        }

        var parameters = new HashSet<string>(constructor.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var stray = record.GetMembers().OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic && p.SetMethod is not null && !p.IsImplicitlyDeclared && !parameters.Contains(p.Name));
        if (stray is not null)
        {
            Report(Descriptors.NotPositionalRecord, stray, record.Name, string.Format(Descriptors.Stray, stray.Name));
            return null;
        }

        var properties = new List<PropertyModel>();
        var jsonNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in constructor.Parameters)
        {
            var type = Wire(parameter.Type, parameter);
            if (type is null)
            {
                return null;
            }

            var jsonName = Naming.CamelCase(parameter.Name);
            if (!jsonNames.Add(jsonName))
            {
                Report(Descriptors.DuplicateJsonName, parameter, record.Name, jsonName);
                return null;
            }

            properties.Add(new PropertyModel(parameter.Name, jsonName, type));
        }

        return properties;
    }

    /// <summary>
    /// The primary constructor: the one whose parameters are all properties. A record with no parameter list has only
    /// its implicit and copy constructors, so no values; one whose constructor takes a value it does not keep is SPAR022.
    /// </summary>
    private IMethodSymbol? Constructor(INamedTypeSymbol record)
    {
        var candidates = record.InstanceConstructors
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, record)))
            .ToList();
        var positional = candidates.FirstOrDefault(c => c.Parameters.Length > 0 && c.Parameters.All(p => HasProperty(record, p)));
        if (positional is not null)
        {
            return positional;
        }

        var withValues = candidates.FirstOrDefault(c => c.Parameters.Length > 0);
        if (withValues is not null)
        {
            var missing = withValues.Parameters.First(p => !HasProperty(record, p));
            Report(Descriptors.MissingProperty, missing, record.Name, missing.Name);
            return null;
        }

        var empty = candidates.FirstOrDefault(c => c.Parameters.Length == 0);
        if (empty is null)
        {
            Report(Descriptors.NotPositionalRecord, record, record.Name, Descriptors.NotPositional);
        }

        return empty;
    }

    private static bool HasProperty(INamedTypeSymbol record, IParameterSymbol parameter) =>
        record.GetMembers(parameter.Name).OfType<IPropertySymbol>().Any();

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

        var name = TypeName(type, $"Error type '{type.Name}'");
        if (name is null || !CasesNested(type))
        {
            return null;
        }

        var cases = Cases(type);
        if (cases is null)
        {
            return null;
        }

        if (cases.Count == 0)
        {
            Report(Descriptors.NoErrorCases, location, type.Name);
            return null;
        }

        Remember(csharp, type);
        var error = new ErrorModel(csharp, "Error_" + Naming.Identifier(csharp), name, cases.ToEquatable());
        _errors[csharp] = error;
        return error;
    }

    private List<ErrorCaseModel>? Cases(INamedTypeSymbol type)
    {
        var cases = new List<ErrorCaseModel>();
        var codes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var nested in type.GetTypeMembers().Where(t => SymbolEqualityComparer.Default.Equals(t.BaseType, type)))
        {
            if (!nested.IsRecord || !nested.IsSealed || nested.IsGenericType)
            {
                Report(Descriptors.BadErrorCase, nested, $"{type.Name}.{nested.Name}");
                return null;
            }

            var explicitCode = ExplicitName(nested, "SparNameAttribute", out var attribute);
            var code = explicitCode ?? Naming.CamelCase(nested.Name);
            if (!ClientNames.IsValidLowerName(code))
            {
                var fix = explicitCode is null ? "Give it a code with [SparName(\"…\")]" : "Use another code in [SparName(\"…\")]";
                Report(Descriptors.InvalidName, nested, attribute, $"Error case '{type.Name}.{nested.Name}'", code, LowerRule, fix);
                return null;
            }

            if (codes.TryGetValue(code, out var earlier))
            {
                Report(Descriptors.DuplicateCode, nested, $"{earlier.Name}, {nested.Name}", type.Name, code);
                return null;
            }

            codes[code] = nested;
            var properties = Properties(nested);
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

            cases.Add(new ErrorCaseModel(nested.ToDisplayString(CSharpFormat), code, properties.ToEquatable()));
        }

        return cases;
    }

    /// <summary>
    /// A case that derives from the error type outside it is SPAR031: the cases are read from the nested types, so it
    /// would never reach the page. Only an error type declared in this compilation is searched.
    /// </summary>
    private bool CasesNested(INamedTypeSymbol type)
    {
        if (!type.Locations.Any(l => l.IsInSource))
        {
            return true;
        }

        var outside = AllTypes(type.ContainingAssembly.GlobalNamespace)
            .Where(t => SymbolEqualityComparer.Default.Equals(t.BaseType, type) && !SymbolEqualityComparer.Default.Equals(t.ContainingType, type))
            .ToList();
        foreach (var derived in outside)
        {
            Report(Descriptors.CaseNotNested, derived, derived.Name, type.Name);
        }

        return outside.Count == 0;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    pending.Push(member);
                }

                continue;
            }

            var type = (INamedTypeSymbol)current;
            yield return type;
            foreach (var nested in type.GetTypeMembers())
            {
                pending.Push(nested);
            }
        }
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

    /// <summary>The name an attribute sets, with the attribute that set it; neither when it sets none.</summary>
    private static string? ExplicitName(ISymbol symbol, string attribute, out AttributeData? data)
    {
        var found = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == attribute);
        var isNamed = found is { ConstructorArguments.Length: > 0 } && found.ConstructorArguments[0].Value is string { Length: > 0 };
        data = isNamed ? found : null;
        return isNamed ? (string)found!.ConstructorArguments[0].Value! : null;
    }

    private static int TimeoutSeconds(IMethodSymbol method)
    {
        var data = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "SparCommandAttribute");
        var argument = data?.NamedArguments.FirstOrDefault(a => a.Key == "TimeoutSeconds");
        return argument?.Value.Value is int seconds ? seconds : 0;
    }

    private void Remember(string csharp, INamedTypeSymbol symbol)
    {
        _named[csharp] = symbol;
        if (_symbols is not null)
        {
            _symbols[csharp] = symbol;
        }
    }

    private static string Display(string csharp) => csharp.Replace("global::", "");

    private void Report(DiagnosticDescriptor descriptor, ISymbol symbol, params object[] arguments) =>
        Report(descriptor, symbol, null, arguments);

    /// <summary>Reports at the attribute that set a name when there is one, else at the symbol.</summary>
    private void Report(DiagnosticDescriptor descriptor, ISymbol symbol, AttributeData? attribute, params object[] arguments)
    {
        if (_report is null)
        {
            return;
        }

        var location = attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? symbol.Locations.FirstOrDefault(l => l.IsInSource)
            ?? Location.None;
        _report(symbol, Diagnostic.Create(descriptor, location, arguments));
    }
}
