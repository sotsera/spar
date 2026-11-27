using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Spar.Generator;

/// <summary>The wire kinds of plan section 2's wire-rules table.</summary>
internal enum WireKind
{
    String,
    Boolean,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal,
    Char,
    Guid,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    TimeSpan,

    /// <summary><c>byte[]</c>: padded base64, <c>Uint8Array</c> in TypeScript; never a list of numbers.</summary>
    Bytes,

    /// <summary>
    /// <c>Spar.SparBlob</c>: a handle, <c>{"id","contentType","size","name"}</c>, written as the result goes out; results
    /// only (SPAR026).
    /// </summary>
    Blob,

    /// <summary>
    /// <c>Spar.FileGrant</c>: <c>{"id","name","size","access"}</c> to the page; read back by its id, resolved to the
    /// call's window's own grant. Not in settings records (SPAR028).
    /// </summary>
    FileGrant,

    /// <summary><c>Spar.FolderGrant</c>: <c>{"id","name","access"}</c>, otherwise as <see cref="FileGrant"/>.</summary>
    FolderGrant,

    /// <summary>An enum: one member name, exactly; a union of string literals in TypeScript.</summary>
    Enum,

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

/// <summary>An enum on the wire: its TypeScript name and its member names, in declaration order.</summary>
internal sealed record EnumModel(string CSharp, string Key, string Name, EquatableArray<string> Members);

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

    /// <summary><c>IAsyncEnumerable&lt;T&gt;</c>: a stream of values (plan section 2c).</summary>
    Items,

    /// <summary><c>IAsyncEnumerable&lt;Result&lt;T, TError&gt;&gt;</c>: a stream whose first error ends it, in-band.</summary>
    ResultItems,
}

/// <summary>
/// A stream command's <c>[SparSubscription]</c>: the buffer's size (256 when the attribute leaves it at 0, 1 when it keeps
/// only the newest value), whether it keeps only the newest value, and whether the page may subscribe again by itself.
/// </summary>
internal sealed record SubscriptionModel(int Capacity, bool LatestOnly, bool Automatic);

internal sealed record CommandModel(
    string MethodName,
    string WireName,
    TypeRef? Request,
    string? RequestName,
    bool TakesToken,
    ReturnShape Return,
    TypeRef? Value,
    string? ErrorKey,
    string? ErrorCSharp,
    int TimeoutSeconds,
    SubscriptionModel? Subscription = null,
    string? SerialGroup = null,
    bool IsResource = false,
    bool ValueHoldsBlob = false,
    bool RequestHoldsGrant = false)
{
    /// <summary>Whether the command is a stream or a subscription: its items travel in batches, with credits.</summary>
    public bool IsStream => Return is ReturnShape.Items or ReturnShape.ResultItems;
}

internal sealed record ContractModel(
    string CSharp,
    string Namespace,
    string WireName,
    string PascalName,
    bool IsInterface,
    EquatableArray<CommandModel> Commands,
    EquatableArray<TypeRef> Types,
    EquatableArray<RecordModel> Records,
    EquatableArray<ErrorModel> Errors,
    EquatableArray<EnumModel> Enums);

internal sealed record EntryPointModel(bool TopLevel, bool ReturnsInt, string TypeName, string MethodName);

internal sealed record CreateCallModel(int Version, string Data);

/// <summary>An <c>app.Run()</c> call in the app, which the generator hands the desktop host the build references.</summary>
internal sealed record RunCallModel(int Version, string Data);

/// <summary>The desktop hosts the compilation references, by their host type's full name.</summary>
internal sealed record HostsModel(bool Windows, bool Linux, bool Loopback);

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

/// <summary>A settings record that <c>spar.Settings(…)</c> names: its key, and the types its serializer needs.</summary>
internal sealed record SettingsModel(
    string CSharp, string Key, EquatableArray<TypeRef> Types, EquatableArray<RecordModel> Records, EquatableArray<EnumModel> Enums);
