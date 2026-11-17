using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Generator;

/// <summary>The primitives of the wire-rules table (plan section 2), and enums: one member name, exactly.</summary>
internal sealed partial class ContractAnalysis
{
    /// <summary>An enum and its member names, or SPAR025 when one name cannot stand for each value.</summary>
    private TypeRef? Enum(INamedTypeSymbol type)
    {
        var csharp = type.ToDisplayString(CSharpFormat);
        var key = "Enum_" + Naming.Identifier(csharp);
        var reference = new TypeRef(WireKind.Enum, csharp, key, null, true);
        if (_enums.ContainsKey(csharp))
        {
            return reference;
        }

        var problem = EnumProblem(type);
        if (problem is not null)
        {
            Report(Descriptors.UnsupportedEnum, type, type.Name, problem);
            return null;
        }

        var name = TypeName(type, $"Enum '{type.Name}'");
        if (name is null)
        {
            return null;
        }

        Remember(csharp, type);
        _enums[csharp] = new EnumModel(csharp, key, name, Members(type).Select(m => m.Name).ToEquatable());
        return reference;
    }

    /// <summary>Why the enum cannot travel as one member name, or null when it can.</summary>
    private static string? EnumProblem(INamedTypeSymbol type)
    {
        if (type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"))
        {
            return "it is [Flags], and one member name cannot carry a combination of them";
        }

        var members = Members(type).ToList();
        if (members.Count == 0)
        {
            return "it has no members";
        }

        var shared = members.GroupBy(m => m.ConstantValue).FirstOrDefault(g => g.Count() > 1);
        return shared is null
            ? null
            : $"its members {string.Join(" and ", shared.Select(m => m.Name))} have one value, which would have two names";
    }

    private static WireKind? Primitive(ITypeSymbol type)
    {
        var special = type.SpecialType switch
        {
            SpecialType.System_String => WireKind.String,
            SpecialType.System_Boolean => WireKind.Boolean,
            SpecialType.System_Byte => WireKind.Byte,
            SpecialType.System_SByte => WireKind.SByte,
            SpecialType.System_Int16 => WireKind.Int16,
            SpecialType.System_UInt16 => WireKind.UInt16,
            SpecialType.System_Int32 => WireKind.Int32,
            SpecialType.System_UInt32 => WireKind.UInt32,
            SpecialType.System_Int64 => WireKind.Int64,
            SpecialType.System_UInt64 => WireKind.UInt64,
            SpecialType.System_Single => WireKind.Single,
            SpecialType.System_Double => WireKind.Double,
            SpecialType.System_Decimal => WireKind.Decimal,
            SpecialType.System_Char => WireKind.Char,
            SpecialType.System_DateTime => WireKind.DateTime,
            _ => (WireKind?)null,
        };
        if (special is not null)
        {
            return special;
        }

        return type.ToDisplayString() switch
        {
            "System.Guid" => WireKind.Guid,
            "System.DateTimeOffset" => WireKind.DateTimeOffset,
            "System.DateOnly" => WireKind.DateOnly,
            "System.TimeOnly" => WireKind.TimeOnly,
            "System.TimeSpan" => WireKind.TimeSpan,
            _ => null,
        };
    }

    private static bool IsByteArray(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };

    /// <summary>Whether the type is <c>DateTime</c>, or a list or nullable of one, at any depth.</summary>
    private static bool MentionsDateTime(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_DateTime)
        {
            return true;
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return MentionsDateTime(nullable.TypeArguments[0]);
        }

        return List(type) is { } element && MentionsDateTime(element);
    }

    /// <summary>The members in declaration order.</summary>
    private static IEnumerable<IFieldSymbol> Members(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue);
}
