using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spar.Generator;

/// <summary>
/// Builds a <see cref="ContractModel"/> from a contract type. The source generator, the diagnostic analyzer and the
/// client tool all call this, so the C# dispatch, the reported diagnostics and the TypeScript client always describe
/// the same contract.
/// </summary>
public static class ContractReader
{
    public const string ContractAttribute = "Spar.SparContractAttribute";
    public const string CommandAttribute = "Spar.SparCommandAttribute";
    public const string NameAttribute = "Spar.SparNameAttribute";
    public const string RuntimeAssembly = "Spar.Runtime";

    private const string ResultMetadataName = "Spar.Result`2";

    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly string[] ReservedPayloadNames = ["kind", "code", "message", "execution"];

    public static bool IsContractAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == ContractAttribute;

    /// <param name="referenced">True for a contract read from a referenced assembly: diagnostics carry no location.</param>
    public static ContractModel Read(
        INamedTypeSymbol type, AttributeData attribute, Compilation compilation, bool referenced, CancellationToken cancellationToken)
    {
        var context = new ReadContext(compilation, referenced ? type.ContainingAssembly.Name : null, cancellationToken);
        var location = context.LocationOf(type);
        context.Anchor = location;

        var explicitName = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name
            ? name
            : null;
        var wireName = explicitName ?? Naming.ContractName(type);
        var form = type.TypeKind == TypeKind.Interface ? ContractForm.Interface : ContractForm.Class;
        var valid = true;

        if (form == ContractForm.Class && (type.TypeKind != TypeKind.Class || !type.IsSealed || type.IsStatic || type.IsRecord))
        {
            context.Report(SparDiagnostics.ContractKind, type,
                $"Contract '{type.Name}' must be a sealed class or an interface.");
            valid = false;
        }

        if (type.IsGenericType)
        {
            context.Report(SparDiagnostics.ContractGeneric, type, $"Contract '{type.Name}' must not be generic.");
            valid = false;
        }

        var attributeLocation = context.LocationOf(attribute) ?? location;
        if (explicitName is not null && !Naming.IsWireName(explicitName))
        {
            context.ReportAt(SparDiagnostics.InvalidName, attributeLocation,
                $"'{explicitName}' is not a valid contract name. Use a camelCase identifier of ASCII letters and digits, such as 'copyJobs'.");
            valid = false;
        }
        else if (Naming.IsReservedContractName(wireName))
        {
            context.ReportAt(SparDiagnostics.ReservedName, explicitName is null ? location : attributeLocation,
                $"Contract '{type.Name}' resolves to the name '{wireName}', which is reserved in JavaScript or by Spar. "
                + "Give it another name with [SparContract(\"…\")].");
            valid = false;
        }

        var commands = new List<(CommandModel Command, LocationModel? Location)>();
        if (valid)
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCommand(method, form))
                {
                    continue;
                }

                context.Anchor = context.LocationOf(method) ?? location;
                var command = ReadCommand(method, context);
                if (command is not null)
                {
                    commands.Add((command, context.Anchor));
                }
            }

            context.Anchor = location;
        }

        // Two methods with one wire name (overloads, "Get" and "GetAsync", or an override): neither is exported.
        var duplicates = commands.GroupBy(c => c.Command.WireName, System.StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        foreach (var group in duplicates)
        {
            var methods = string.Join(", ", group.Select(c => c.Command.MethodName));
            foreach (var (command, commandLocation) in group)
            {
                context.ReportAt(SparDiagnostics.DuplicateCommand, commandLocation,
                    $"Commands {methods} of contract '{wireName}' {(group.Count() == 2 ? "both" : "all")} resolve to the name '{command.WireName}'. "
                    + "Give them distinct names with [SparCommand(\"…\")].");
            }
        }

        var exported = commands.Select(c => c.Command)
            .Where(c => !duplicates.Any(g => g.Key == c.WireName))
            .ToList();
        var (dtos, errors) = context.Closure(exported);
        return new ContractModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.Name,
            wireName,
            explicitName is not null,
            form,
            referenced ? type.ContainingAssembly.Name : null,
            location,
            valid,
            exported.ToEquatableArray(),
            dtos.ToEquatableArray(),
            errors.ToEquatableArray(),
            context.Diagnostics.ToEquatableArray());
    }

    private static bool IsCommand(IMethodSymbol method, ContractForm form) =>
        method.MethodKind == MethodKind.Ordinary
        && !method.IsStatic
        && (form == ContractForm.Interface
            ? method.IsAbstract
            : method.DeclaredAccessibility == Accessibility.Public && !method.IsOverride);

    private static CommandModel? ReadCommand(IMethodSymbol method, ReadContext context)
    {
        var commandAttribute = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CommandAttribute);
        var explicitName = commandAttribute?.ConstructorArguments.FirstOrDefault().Value as string;
        var wireName = explicitName ?? Naming.CommandName(method.Name);
        var ok = true;

        if (explicitName is not null && !Naming.IsWireName(explicitName))
        {
            context.ReportAt(SparDiagnostics.InvalidName, context.LocationOf(commandAttribute!) ?? context.Anchor,
                $"'{explicitName}' is not a valid command name. Use a camelCase identifier of ASCII letters and digits, such as 'getPlan'.");
            ok = false;
        }

        if (method.IsGenericMethod)
        {
            context.Report(SparDiagnostics.GenericCommand, method, $"Command '{method.Name}' must not be generic.");
            return null;
        }

        // Parameters: zero or one request, then an optional CancellationToken.
        IParameterSymbol? request = null;
        var takesCancellation = false;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None || parameter.IsParams)
            {
                context.Report(SparDiagnostics.ParameterModifier, parameter,
                    $"Parameter '{parameter.Name}' of command '{method.Name}' cannot be ref, out, in or params.");
                ok = false;
                continue;
            }

            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                if (i != method.Parameters.Length - 1)
                {
                    context.Report(SparDiagnostics.CancellationPosition, parameter,
                        $"The CancellationToken of command '{method.Name}' must be its last parameter.");
                    ok = false;
                }

                takesCancellation = true;
                continue;
            }

            if (request is not null)
            {
                context.Report(SparDiagnostics.TooManyParameters, parameter,
                    $"Command '{method.Name}' takes at most one request parameter, then a CancellationToken. "
                    + "Put the values in one record, such as 'record GetPlanRequest(string Id, int Version)'.");
                ok = false;
                continue;
            }

            request = parameter;
        }

        TypeRef? requestType = null;
        if (request is not null)
        {
            requestType = context.ReadType(request.Type, request);
            if (requestType is null)
            {
                ok = false;
            }
            else if (requestType.Kind is WireKind.Array)
            {
                context.Report(SparDiagnostics.RequestType, request,
                    $"The request of command '{method.Name}' is '{request.Type.ToDisplayString()}'. "
                    + "A request is one record or one scalar (string, int, long, double, bool or Guid).");
                ok = false;
            }
        }

        // Return: Task<T>, Task<Result<T, TError>> or IAsyncEnumerable<T>.
        var returnLocation = context.ReturnTypeLocation(method);
        var kind = CommandKind.Unary;
        ITypeSymbol? response = null;
        INamedTypeSymbol? result = null;
        if (method.ReturnType is INamedTypeSymbol { IsGenericType: true } returnType)
        {
            var definition = returnType.ConstructedFrom.ToDisplayString();
            if (definition == "System.Threading.Tasks.Task<TResult>")
            {
                response = returnType.TypeArguments[0];
                result = IsResult(response) ? (INamedTypeSymbol)response : null;
            }
            else if (definition == "System.Collections.Generic.IAsyncEnumerable<T>")
            {
                kind = CommandKind.Stream;
                response = returnType.TypeArguments[0];
                if (IsResult(response))
                {
                    context.ReportAt(SparDiagnostics.ReturnType, returnLocation,
                        $"Command '{method.Name}' streams Result items. Result<T, TError> is only supported as Task<Result<T, TError>>.");
                    return null;
                }
            }
        }

        if (response is null)
        {
            context.ReportAt(SparDiagnostics.ReturnType, returnLocation,
                $"Command '{method.Name}' returns '{method.ReturnType.ToDisplayString()}'. "
                + "A command returns Task<T>, Task<Result<T, TError>> or IAsyncEnumerable<T>.");
            return null;
        }

        var saved = context.Anchor;
        context.Anchor = returnLocation;
        TypeRef? responseType;
        TypeRef? errorType = null;
        if (result is not null)
        {
            responseType = context.ReadType(result.TypeArguments[0], null);
            errorType = context.ReadError(result.TypeArguments[1]);
            ok &= errorType is not null;
        }
        else
        {
            responseType = context.ReadType(response, null);
        }

        context.Anchor = saved;
        if (!ok || responseType is null)
        {
            return null;
        }

        return new CommandModel(method.Name, wireName, requestType, responseType, errorType, takesCancellation, kind, context.Anchor);
    }

    private static bool IsResult(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named
        && named.ConstructedFrom.ContainingAssembly?.Name == RuntimeAssembly
        && named.ConstructedFrom.ContainingNamespace.ToDisplayString() + "." + named.ConstructedFrom.MetadataName == ResultMetadataName;

    private static string? ExplicitTypeName(INamedTypeSymbol type) =>
        type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NameAttribute)?
            .ConstructorArguments.FirstOrDefault().Value as string;

    private sealed class ReadContext(Compilation compilation, string? referencedAssembly, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, DtoModel> _dtos = [];
        private readonly Dictionary<string, ErrorModel> _errors = [];
        private readonly HashSet<string> _failed = [];
        private readonly HashSet<string> _inProgress = [];

        public List<DiagnosticModel> Diagnostics { get; } = [];

        /// <summary>Where diagnostics go when the offending symbol has no location in this compilation.</summary>
        public LocationModel? Anchor { get; set; }

        public void Report(string id, ISymbol symbol, string message) => ReportAt(id, LocationOf(symbol) ?? Anchor, message);

        public void ReportAt(string id, LocationModel? location, string message)
        {
            if (referencedAssembly is not null)
            {
                Diagnostics.Add(new DiagnosticModel(id, $"In referenced assembly '{referencedAssembly}': {message}", null));
                return;
            }

            var diagnostic = new DiagnosticModel(id, message, location);
            if (!Diagnostics.Contains(diagnostic))
            {
                Diagnostics.Add(diagnostic);
            }
        }

        public TypeRef? ReadType(ITypeSymbol type, ISymbol? owner)
        {
            var nullable = type.NullableAnnotation == NullableAnnotation.Annotated;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapped)
            {
                type = wrapped.TypeArguments[0];
                nullable = true;
            }

            var csharp = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(Qualified);
            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                    return new TypeRef(WireKind.String, csharp, nullable, null, null);
                case SpecialType.System_Int32:
                    return new TypeRef(WireKind.Int32, csharp, nullable, null, null);
                case SpecialType.System_Int64:
                    return new TypeRef(WireKind.Int64, csharp, nullable, null, null);
                case SpecialType.System_Double:
                    return new TypeRef(WireKind.Double, csharp, nullable, null, null);
                case SpecialType.System_Boolean:
                    return new TypeRef(WireKind.Boolean, csharp, nullable, null, null);
            }

            if (csharp == "global::System.Guid")
            {
                return new TypeRef(WireKind.Guid, csharp, nullable, null, null);
            }

            var element = type switch
            {
                IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
                INamedTypeSymbol { IsGenericType: true } generic
                    when generic.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>" =>
                    generic.TypeArguments[0],
                _ => null,
            };
            if (element is not null)
            {
                var elementRef = ReadType(element, owner);
                return elementRef is null ? null : new TypeRef(WireKind.Array, csharp, nullable, elementRef, null);
            }

            if (IsResult(type))
            {
                ReportOn(SparDiagnostics.UnsupportedType, owner,
                    $"'{type.ToDisplayString()}' is only supported as a command's return type, Task<Result<T, TError>>.");
                return null;
            }

            if (type is INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Class } record)
            {
                var key = record.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (_failed.Contains(key))
                {
                    return null;
                }

                if (!_dtos.ContainsKey(key) && _inProgress.Add(key))
                {
                    var dto = ReadDto(record, key);
                    _inProgress.Remove(key);
                    if (dto is null)
                    {
                        _failed.Add(key);
                        return null;
                    }

                    _dtos[key] = dto;
                }

                return new TypeRef(WireKind.Dto, csharp, nullable, null, key);
            }

            ReportOn(SparDiagnostics.UnsupportedType, owner,
                $"Type '{type.ToDisplayString()}' is not a supported wire type. Use string, int, long, double, bool, Guid, "
                + "a positional record, or an array or IReadOnlyList<T> of these.");
            return null;
        }

        public TypeRef? ReadError(ITypeSymbol type)
        {
            var csharp = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(Qualified);
            if (type is not INamedTypeSymbol named)
            {
                ReportAt(SparDiagnostics.ErrorNotClosed, Anchor, $"Error type '{type.ToDisplayString()}' must be a closed record.");
                return null;
            }

            var key = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (_failed.Contains(key))
            {
                return null;
            }

            if (!_errors.ContainsKey(key))
            {
                var error = ReadErrorModel(named, key);
                if (error is null)
                {
                    _failed.Add(key);
                    return null;
                }

                _errors[key] = error;
            }

            return new TypeRef(WireKind.Error, csharp, false, null, key);
        }

        /// <summary>The named types reachable from the given commands, in a stable order.</summary>
        public (List<DtoModel> Dtos, List<ErrorModel> Errors) Closure(IEnumerable<CommandModel> commands)
        {
            var dtos = new SortedDictionary<string, DtoModel>(System.StringComparer.Ordinal);
            var errors = new SortedDictionary<string, ErrorModel>(System.StringComparer.Ordinal);
            var pending = new Stack<TypeRef>();
            foreach (var command in commands)
            {
                if (command.Request is { } request)
                {
                    pending.Push(request);
                }

                pending.Push(command.Response);
                if (command.Error is { } error)
                {
                    pending.Push(error);
                }
            }

            while (pending.Count > 0)
            {
                var type = pending.Pop();
                if (type.Element is { } element)
                {
                    pending.Push(element);
                }
                else if (type.Kind == WireKind.Dto && !dtos.ContainsKey(type.TypeKey!) && _dtos.TryGetValue(type.TypeKey!, out var dto))
                {
                    dtos[dto.Key] = dto;
                    foreach (var property in dto.Properties)
                    {
                        pending.Push(property.Type);
                    }
                }
                else if (type.Kind == WireKind.Error && !errors.ContainsKey(type.TypeKey!) && _errors.TryGetValue(type.TypeKey!, out var error))
                {
                    errors[error.Key] = error;
                    foreach (var property in error.Cases.SelectMany(c => c.Properties))
                    {
                        pending.Push(property.Type);
                    }
                }
            }

            return ([.. dtos.Values], [.. errors.Values]);
        }

        public LocationModel? LocationOf(ISymbol symbol) => ToLocation(symbol.Locations.FirstOrDefault());

        public LocationModel? LocationOf(AttributeData attribute) =>
            attribute.ApplicationSyntaxReference is { } reference
                ? ToLocation(reference.GetSyntax(cancellationToken).GetLocation())
                : null;

        public LocationModel? ReturnTypeLocation(IMethodSymbol method)
        {
            var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
            return syntax is MethodDeclarationSyntax declaration
                ? ToLocation(declaration.ReturnType.GetLocation()) ?? LocationOf(method)
                : LocationOf(method) ?? Anchor;
        }

        private DtoModel? ReadDto(INamedTypeSymbol record, string key)
        {
            var tsName = ReadTsName(record);
            if (tsName is null)
            {
                return null;
            }

            if (record.IsAbstract && ClosedTypes.IsClosed(record, compilation, cancellationToken))
            {
                Report(SparDiagnostics.RecordShape, record,
                    $"'{record.Name}' is a closed error record. Return it as the error of Task<Result<T, {record.Name}>>, not as a value.");
                return null;
            }

            if (record.IsGenericType || record.IsAbstract)
            {
                Report(SparDiagnostics.RecordShape, record,
                    $"Record '{record.Name}' must be a non-generic, non-abstract positional record to be sent on the wire.");
                return null;
            }

            var properties = ReadPositionalProperties(record, isErrorCase: false);
            return properties is null ? null : new DtoModel(key, tsName, properties.ToEquatableArray(), LocationOf(record));
        }

        private ErrorModel? ReadErrorModel(INamedTypeSymbol type, string key)
        {
            if (!type.IsRecord || type.TypeKind != TypeKind.Class || type.IsGenericType
                || !ClosedTypes.IsClosed(type, compilation, cancellationToken))
            {
                Report(SparDiagnostics.ErrorNotClosed, type,
                    $"Error type '{type.Name}' must be a non-generic closed record with nested sealed cases, "
                    + $"such as 'public closed record {type.Name} {{ public sealed record NotFound(string Id) : {type.Name}; }}'.");
                return null;
            }

            var tsName = ReadTsName(type);
            if (tsName is null)
            {
                return null;
            }

            var ok = true;
            var cases = new List<(ErrorCaseModel Case, INamedTypeSymbol Symbol)>();
            foreach (var derived in ClosedTypes.FindDerived(type, cancellationToken))
            {
                if (!SymbolEqualityComparer.Default.Equals(derived.ContainingType, type))
                {
                    Report(SparDiagnostics.ErrorCaseOutside, derived,
                        $"Error case '{derived.Name}' derives from '{type.Name}' but is not nested in it. "
                        + $"Declare every case of a closed error record inside it: 'public sealed record {derived.Name}(…) : {type.Name};'.");
                    ok = false;
                    continue;
                }

                if (!derived.IsRecord || !derived.IsSealed || derived.IsGenericType)
                {
                    Report(SparDiagnostics.ErrorCaseShape, derived,
                        $"Error case '{type.Name}.{derived.Name}' must be a non-generic sealed record.");
                    ok = false;
                    continue;
                }

                var code = ExplicitTypeName(derived) ?? Naming.ToCamelCase(derived.Name);
                if (!Naming.IsTypeScriptName(code))
                {
                    Report(SparDiagnostics.InvalidName, derived,
                        $"'{code}' is not a valid error code for '{type.Name}.{derived.Name}'. Use an identifier of ASCII letters and digits.");
                    ok = false;
                    continue;
                }

                var properties = ReadPositionalProperties(derived, isErrorCase: true);
                if (properties is null)
                {
                    ok = false;
                    continue;
                }

                cases.Add((new ErrorCaseModel(derived.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), code,
                    properties.ToEquatableArray()), derived));
            }

            foreach (var group in cases.GroupBy(c => c.Case.Code, System.StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                var names = string.Join(", ", group.Select(c => c.Symbol.Name));
                foreach (var (_, symbol) in group.Skip(1))
                {
                    Report(SparDiagnostics.DuplicateErrorCode, symbol,
                        $"Error cases {names} of '{type.Name}' {(group.Count() == 2 ? "both" : "all")} resolve to the code '{group.Key}'. Give one a distinct code with [SparName(\"…\")].");
                }

                ok = false;
            }

            if (ok && cases.Count == 0)
            {
                Report(SparDiagnostics.ErrorWithoutCases, type,
                    $"Error type '{type.Name}' has no cases. Nest at least one 'public sealed record …(…) : {type.Name};' in it.");
                ok = false;
            }

            return ok
                ? new ErrorModel(key, tsName, cases.Select(c => c.Case).OrderBy(c => c.Code, System.StringComparer.Ordinal).ToEquatableArray(), LocationOf(type))
                : null;
        }

        private List<PropertyModel>? ReadPositionalProperties(INamedTypeSymbol record, bool isErrorCase)
        {
            var constructors = record.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public
                    && !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, record)))
                .ToList();
            if (constructors.Count != 1)
            {
                Report(SparDiagnostics.RecordShape, record,
                    $"Record '{record.Name}' needs exactly one public constructor: declare it as a positional record.");
                return null;
            }

            var ok = true;
            var properties = new List<PropertyModel>();
            var covered = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var parameter in constructors[0].Parameters)
            {
                var property = record.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault();
                if (property is null)
                {
                    Report(SparDiagnostics.RecordProperty, parameter,
                        $"Record '{record.Name}' has no property for constructor parameter '{parameter.Name}'.");
                    ok = false;
                    continue;
                }

                covered.Add(property.Name);
                var jsonName = Naming.ToCamelCase(property.Name);
                if (isErrorCase && ReservedPayloadNames.Contains(jsonName))
                {
                    Report(SparDiagnostics.ReservedPayloadName, parameter,
                        $"Error case '{record.Name}' cannot have a payload named '{jsonName}': 'kind', 'code', 'message' and "
                        + "'execution' are reserved for the error object. Rename the parameter.");
                    ok = false;
                    continue;
                }

                var type = ReadType(parameter.Type, parameter);
                if (type is null)
                {
                    ok = false;
                    continue;
                }

                if (properties.Any(p => p.JsonName == jsonName))
                {
                    Report(SparDiagnostics.DuplicateProperty, parameter,
                        $"Record '{record.Name}' has two properties with the JSON name '{jsonName}'.");
                    ok = false;
                    continue;
                }

                properties.Add(new PropertyModel(property.Name, jsonName, type));
            }

            // A settable or init-only property outside the constructor would silently not travel.
            foreach (var extra in record.GetMembers().OfType<IPropertySymbol>().Where(p =>
                p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer
                && p.Name != "EqualityContract" && !covered.Contains(p.Name)))
            {
                Report(SparDiagnostics.RecordShape, extra,
                    $"Property '{record.Name}.{extra.Name}' is not a constructor parameter, so it would not be sent. "
                    + "Declare it as a parameter of the positional record.");
                ok = false;
            }

            return ok ? properties : null;
        }

        private string? ReadTsName(INamedTypeSymbol type)
        {
            var explicitName = ExplicitTypeName(type);
            if (explicitName is not null && !Naming.IsTypeScriptName(explicitName))
            {
                Report(SparDiagnostics.InvalidName, type,
                    $"'{explicitName}' is not a valid TypeScript name for '{type.Name}'. Use an identifier of ASCII letters and digits.");
                return null;
            }

            return explicitName ?? type.Name;
        }

        private void ReportOn(string id, ISymbol? owner, string message) =>
            ReportAt(id, owner is null ? Anchor : LocationOf(owner) ?? Anchor, message);

        private LocationModel? ToLocation(Location? location)
        {
            // Only locations in this compilation: in the IDE a project reference brings symbols with syntax that belongs
            // to another project, and reporting there would fail.
            if (referencedAssembly is not null || location is null || !location.IsInSource
                || location.SourceTree is null || !compilation.ContainsSyntaxTree(location.SourceTree))
            {
                return null;
            }

            var span = location.GetLineSpan();
            return new LocationModel(
                span.Path,
                location.SourceSpan.Start,
                location.SourceSpan.Length,
                span.StartLinePosition.Line,
                span.StartLinePosition.Character,
                span.EndLinePosition.Line,
                span.EndLinePosition.Character);
        }
    }
}
