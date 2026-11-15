using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Spar.Generator;

/// <summary>The wire kinds the generator supports today; the full set is plan section 2's wire-rules table.</summary>
internal enum WireKind
{
    String,
    Boolean,
    Int32,
    Int64,
    Double,
    Guid,
    DateTimeOffset,
    List,
    Record,
    Nullable,
}

/// <summary>One wire type: its C# spelling, and a key that names its generated reader and writer.</summary>
internal sealed record TypeRef(WireKind Kind, string CSharp, string Key, TypeRef? Element, bool IsValueType);

internal sealed record PropertyModel(string Name, string JsonName, TypeRef Type);

/// <summary>A record; <paramref name="Name"/> is its TypeScript name, from <c>[SparName]</c> or its own.</summary>
internal sealed record RecordModel(string CSharp, string Key, string Name, EquatableArray<PropertyModel> Properties);

internal sealed record ErrorCaseModel(string CSharp, string Code, EquatableArray<PropertyModel> Properties);

/// <summary>An error type; <paramref name="Name"/> is its TypeScript name, from <c>[SparName]</c> or its own.</summary>
internal sealed record ErrorModel(string CSharp, string Key, string Name, EquatableArray<ErrorCaseModel> Cases);

internal enum ReturnShape
{
    /// <summary><c>Task</c> or <c>ValueTask</c>: success with no value.</summary>
    None,

    /// <summary><c>Task&lt;T&gt;</c>: a value.</summary>
    Value,

    /// <summary><c>Task&lt;Result&lt;T, TError&gt;&gt;</c>.</summary>
    ResultWithValue,

    /// <summary><c>Task&lt;Result&lt;TError&gt;&gt;</c>.</summary>
    ResultWithoutValue,
}

internal sealed record CommandModel(
    string MethodName, string WireName, TypeRef? Request, string? RequestName, bool TakesToken, ReturnShape Return, TypeRef? Value, string? ErrorKey, string? ErrorCSharp, int TimeoutSeconds);

internal sealed record ContractModel(
    string CSharp,
    string Namespace,
    string WireName,
    string PascalName,
    bool IsInterface,
    EquatableArray<CommandModel> Commands,
    EquatableArray<TypeRef> Types,
    EquatableArray<RecordModel> Records,
    EquatableArray<ErrorModel> Errors);

internal sealed record EntryPointModel(bool TopLevel, bool ReturnsInt, string TypeName, string MethodName);

internal sealed record CreateCallModel(int Version, string Data);

/// <summary>An immutable array compared by its items, so incremental pipeline steps cache correctly.</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Length => Items.Length;

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatable<T>(this IEnumerable<T> items)
        where T : IEquatable<T> => new(items.ToImmutableArray());
}
