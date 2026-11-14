namespace Spike.Generator;

public enum WireKind
{
    String,
    Int32,
    Int64,
    Double,
    Boolean,
    Array,
    Dto,
}

/// <summary>A supported wire type. <see cref="CSharp"/> is fully qualified with <c>global::</c>.</summary>
public sealed record TypeRef(WireKind Kind, string CSharp, bool Nullable, TypeRef? Element, string? DtoName);

public sealed record PropertyModel(string Name, string JsonName, TypeRef Type);

public sealed record DtoModel(string CSharp, string Name, EquatableArray<PropertyModel> Properties);

public enum CommandKind
{
    Unary,

    /// <summary>Returns <c>IAsyncEnumerable&lt;T&gt;</c>: pulled with back-pressure.</summary>
    Stream,

    /// <summary>A stream marked <c>[SparSubscription]</c>: bounded buffer, drop-oldest, never blocks its producer.</summary>
    Subscription,
}

/// <summary>A command. For streams and subscriptions <see cref="Response"/> is the item type.</summary>
public sealed record CommandModel(string MethodName, string WireName, TypeRef? Request, TypeRef Response, bool TakesCancellation,
    CommandKind Kind = CommandKind.Unary, int Capacity = 0);

public sealed record LocationModel(string Path, int Start, int Length, int Line, int Column, int EndLine, int EndColumn);

public sealed record DiagnosticModel(string Id, string Message, LocationModel? Location);

public sealed record ContractModel(
    string CSharp,
    string TypeName,
    string WireName,
    EquatableArray<CommandModel> Commands,
    EquatableArray<DtoModel> Dtos,
    EquatableArray<DiagnosticModel> Diagnostics);
