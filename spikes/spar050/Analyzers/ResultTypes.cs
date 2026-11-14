using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Spar.Analyzers;

/// <summary>
/// Answers "does a default value of this type hold a default Result?" for one compilation, with a cache. A type holds
/// one when it is Spar's Result struct, or a struct (tuple, record struct, inline array, ValueTask…) with an instance
/// field that holds one. Nullable&lt;Result&gt; does not: its default is null, not a default Result.
/// </summary>
internal sealed class ResultTypes
{
    private const int MaxDepth = 8;

    private readonly ConcurrentDictionary<ITypeSymbol, bool> _cache = new(SymbolEqualityComparer.Default);

    public static bool IsResult(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Struct, Name: "Result", Arity: 1 or 2 } named
        && named.ContainingNamespace is { Name: "Spar", ContainingNamespace.IsGlobalNamespace: true };

    public static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    public static string FullMetadataName(INamedTypeSymbol type)
    {
        var builder = new StringBuilder(type.MetadataName);
        var containing = type.ContainingType;
        while (containing is not null)
        {
            builder.Insert(0, containing.MetadataName + "+");
            containing = containing.ContainingType;
        }

        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false })
        {
            builder.Insert(0, ns.Name + ".");
            ns = ns.ContainingNamespace;
        }

        return builder.ToString();
    }

    public static bool HasCodeAnalysisAttribute(ImmutableArray<AttributeData> attributes, string name)
    {
        foreach (var attribute in attributes)
        {
            if (IsCodeAnalysisAttribute(attribute, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for [MaybeNull] or [MaybeNullWhen(…)] on a parameter: the API may leave default in it.</summary>
    public static bool MayLeaveDefault(IParameterSymbol parameter, out bool? whenValue)
    {
        whenValue = null;
        foreach (var attribute in parameter.GetAttributes())
        {
            if (IsCodeAnalysisAttribute(attribute, "MaybeNullAttribute"))
            {
                return true;
            }

            if (IsCodeAnalysisAttribute(attribute, "MaybeNullWhenAttribute")
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is bool value)
            {
                whenValue = value;
                return true;
            }
        }

        return false;
    }

    public bool HoldsResult(ITypeSymbol? type) => HoldsResult(type, 0);

    /// <summary>"default(Result&lt;…&gt;)" for a Result, or "a default 'Pair' holding default(Result&lt;…&gt;)".</summary>
    public string DescribeDefault(ITypeSymbol type)
    {
        if (IsResult(type))
        {
            return $"default({Display(type)})";
        }

        var result = FindResult(type, 0);
        return result is null
            ? $"default({Display(type)})"
            : $"a default '{Display(type)}' holding default({Display(result)})";
    }

    /// <summary>The Result type itself, or the first Result a struct holds, for messages.</summary>
    public ITypeSymbol? FindResult(ITypeSymbol? type, int depth)
    {
        if (type is null || depth > MaxDepth)
        {
            return null;
        }

        if (IsResult(type))
        {
            return type;
        }

        if (!HoldsResult(type))
        {
            return null;
        }

        foreach (var fieldType in FieldTypes((INamedTypeSymbol)type))
        {
            var found = FindResult(fieldType, depth + 1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsCodeAnalysisAttribute(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } attributeClass
        && attributeClass.Name == name
        && attributeClass.ContainingNamespace?.ToDisplayString() == "System.Diagnostics.CodeAnalysis";

    /// <summary>
    /// The types of a struct's instance fields. The default metadata import hides private fields of metadata structs,
    /// so the BCL wrappers that store a T inline are also listed by name.
    /// </summary>
    private static IEnumerable<ITypeSymbol> FieldTypes(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false, IsConst: false, RefKind: RefKind.None } field)
            {
                yield return field.Type;
            }
        }

        var name = FullMetadataName(type.OriginalDefinition);
        if (name is "System.Collections.Generic.KeyValuePair`2" or "System.Threading.Tasks.ValueTask`1")
        {
            foreach (var argument in type.TypeArguments)
            {
                yield return argument;
            }
        }
    }

    private bool HoldsResult(ITypeSymbol? type, int depth)
    {
        if (type is not INamedTypeSymbol named || depth > MaxDepth)
        {
            return false;
        }

        if (IsResult(named))
        {
            return true;
        }

        // Most defaults in ordinary code are primitives, enums, CancellationToken and classes: answer those first.
        if (named.TypeKind != TypeKind.Struct
            || named.SpecialType != SpecialType.None
            || named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return false;
        }

        if (_cache.TryGetValue(named, out var cached))
        {
            return cached;
        }

        var holds = false;
        foreach (var fieldType in FieldTypes(named))
        {
            if (HoldsResult(fieldType, depth + 1))
            {
                holds = true;
                break;
            }
        }

        _cache.TryAdd(named, holds);
        return holds;
    }
}
