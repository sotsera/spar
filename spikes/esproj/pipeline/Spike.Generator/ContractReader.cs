using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Spike.Generator;

/// <summary>
/// Builds a <see cref="ContractModel"/> from a contract class. The source generator and the
/// out-of-process client tool both call this, so the C# dispatch and the TypeScript client
/// always describe the same contract.
/// </summary>
public static class ContractReader
{
    public const string ContractAttribute = "Spike.SparContractAttribute";
    public const string CommandAttribute = "Spike.SparCommandAttribute";
    public const string SubscriptionAttribute = "Spike.SparSubscriptionAttribute";

    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ContractModel Read(INamedTypeSymbol type, AttributeData attribute, CancellationToken cancellationToken)
    {
        var wireName = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name
            ? name
            : ToCamelCase(type.Name);

        var context = new ReadContext();
        var commands = new List<CommandModel>();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == CommandAttribute))
            {
                continue;
            }

            var command = ReadCommand(method, context);
            if (command is not null)
            {
                commands.Add(command);
            }
        }

        return new ContractModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.Name,
            wireName,
            commands.ToEquatableArray(),
            context.Dtos.Values.ToEquatableArray(),
            context.Diagnostics.ToEquatableArray());
    }

    private static CommandModel? ReadCommand(IMethodSymbol method, ReadContext context)
    {
        const string AsyncEnumerable = "System.Collections.Generic.IAsyncEnumerable<T>";
        if (method.ReturnType is not INamedTypeSymbol { IsGenericType: true } returnType
            || returnType.ConstructedFrom.ToDisplayString() is not ("System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask<TResult>" or AsyncEnumerable))
        {
            context.Report("SPIKE001", $"Command '{method.Name}' must return Task<T>, ValueTask<T> or IAsyncEnumerable<T>.", method);
            return null;
        }

        var kind = CommandKind.Unary;
        var capacity = 0;
        if (returnType.ConstructedFrom.ToDisplayString() == AsyncEnumerable)
        {
            kind = CommandKind.Stream;
            var subscription = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SubscriptionAttribute);
            if (subscription is not null)
            {
                kind = CommandKind.Subscription;
                capacity = subscription.ConstructorArguments.Length == 1 && subscription.ConstructorArguments[0].Value is int value ? value : 0;
                if (capacity < 1)
                {
                    context.Report("SPIKE006", $"Subscription '{method.Name}' needs a capacity of at least 1.", method);
                    return null;
                }
            }
        }

        var parameters = method.Parameters.ToList();
        var takesCancellation = parameters.Count > 0
            && parameters[parameters.Count - 1].Type.ToDisplayString() == "System.Threading.CancellationToken";
        if (takesCancellation)
        {
            parameters.RemoveAt(parameters.Count - 1);
        }

        if (parameters.Count > 1)
        {
            context.Report("SPIKE002", $"Command '{method.Name}' can take at most one request parameter.", method);
            return null;
        }

        TypeRef? request = null;
        if (parameters.Count == 1)
        {
            request = context.ReadType(parameters[0].Type, parameters[0]);
            if (request is null)
            {
                return null;
            }
        }

        var response = context.ReadType(returnType.TypeArguments[0], method);
        if (response is null)
        {
            return null;
        }

        var wireName = method.Name.EndsWith("Async", System.StringComparison.Ordinal)
            ? method.Name.Substring(0, method.Name.Length - "Async".Length)
            : method.Name;

        return new CommandModel(method.Name, ToCamelCase(wireName), request, response, takesCancellation, kind, capacity);
    }

    public static string ToCamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private sealed class ReadContext
    {
        public Dictionary<string, DtoModel> Dtos { get; } = [];

        public List<DiagnosticModel> Diagnostics { get; } = [];

        private readonly HashSet<string> _inProgress = [];

        public void Report(string id, string message, ISymbol symbol) =>
            Diagnostics.Add(new DiagnosticModel(id, message, ToLocation(symbol.Locations.FirstOrDefault())));

        public TypeRef? ReadType(ITypeSymbol type, ISymbol owner)
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

            if (type is INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Class } record)
            {
                var dtoName = record.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!Dtos.ContainsKey(dtoName) && _inProgress.Add(dtoName))
                {
                    var dto = ReadDto(record, dtoName);
                    _inProgress.Remove(dtoName);
                    if (dto is null)
                    {
                        return null;
                    }

                    Dtos[dtoName] = dto;
                }

                return new TypeRef(WireKind.Dto, csharp, nullable, null, dtoName);
            }

            Report("SPIKE003", $"Type '{type.ToDisplayString()}' is not a supported wire type.", owner);
            return null;
        }

        private DtoModel? ReadDto(INamedTypeSymbol record, string dtoName)
        {
            var constructors = record.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0
                    && !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, record)))
                .ToList();
            if (constructors.Count != 1)
            {
                Report("SPIKE004", $"Record '{record.Name}' needs exactly one public positional constructor.", record);
                return null;
            }

            var properties = new List<PropertyModel>();
            foreach (var parameter in constructors[0].Parameters)
            {
                var type = ReadType(parameter.Type, parameter);
                if (type is null)
                {
                    return null;
                }

                var property = record.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(p =>
                    string.Equals(p.Name, parameter.Name, System.StringComparison.OrdinalIgnoreCase));
                if (property is null)
                {
                    Report("SPIKE005", $"Record '{record.Name}' has no property for parameter '{parameter.Name}'.", parameter);
                    return null;
                }

                properties.Add(new PropertyModel(property.Name, ToCamelCase(property.Name), type));
            }

            return new DtoModel(dtoName, record.Name, properties.ToEquatableArray());
        }

        private static LocationModel? ToLocation(Location? location)
        {
            if (location is null || !location.IsInSource)
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
