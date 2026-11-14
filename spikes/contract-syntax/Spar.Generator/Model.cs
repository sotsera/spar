namespace Spar.Generator;

public enum WireKind
{
    String,
    Int32,
    Int64,
    Double,
    Boolean,
    Guid,
    Array,
    Dto,
    Error,
}

/// <summary>
/// A supported wire type. <see cref="CSharp"/> is fully qualified with <c>global::</c>. For records,
/// <see cref="TypeKey"/> identifies the <see cref="DtoModel"/> or <see cref="ErrorModel"/>.
/// </summary>
public sealed record TypeRef(WireKind Kind, string CSharp, bool Nullable, TypeRef? Element, string? TypeKey);

public sealed record PropertyModel(string Name, string JsonName, TypeRef Type);

/// <summary>A positional record sent on the wire. <see cref="Key"/> is its fully qualified C# name.</summary>
public sealed record DtoModel(string Key, string TsName, EquatableArray<PropertyModel> Properties, LocationModel? Location);

/// <summary>One nested sealed case of a closed error record, written as <c>{ "code": …, …payload }</c>.</summary>
public sealed record ErrorCaseModel(string CSharp, string Code, EquatableArray<PropertyModel> Properties);

/// <summary>A <c>closed record</c> used as <c>TError</c> in <c>Result&lt;T, TError&gt;</c>.</summary>
public sealed record ErrorModel(string Key, string TsName, EquatableArray<ErrorCaseModel> Cases, LocationModel? Location);

public enum CommandKind
{
    Unary,

    /// <summary>Returns <c>IAsyncEnumerable&lt;T&gt;</c>: pulled with back-pressure.</summary>
    Stream,
}

/// <summary>
/// A valid command. For streams <see cref="Response"/> is the item type. For <c>Task&lt;Result&lt;T, TError&gt;&gt;</c>,
/// <see cref="Response"/> is <c>T</c> and <see cref="Error"/> is <c>TError</c>.
/// </summary>
public sealed record CommandModel(
    string MethodName,
    string WireName,
    TypeRef? Request,
    TypeRef Response,
    TypeRef? Error,
    bool TakesCancellation,
    CommandKind Kind,
    LocationModel? Location);

public sealed record LocationModel(string Path, int Start, int Length, int Line, int Column, int EndLine, int EndColumn);

public sealed record DiagnosticModel(string Id, string Message, LocationModel? Location);

public enum ContractForm
{
    Class,
    Interface,
}

/// <summary>
/// One contract as read from a type. <see cref="Assembly"/> is null for contracts declared in the compilation being
/// built and names the assembly for contracts read from a reference; those carry no source locations.
/// <see cref="Commands"/> holds only valid commands; invalid ones are reported in <see cref="Diagnostics"/>.
/// <see cref="Dtos"/> and <see cref="Errors"/> are the closure of named types the valid commands use.
/// </summary>
public sealed record ContractModel(
    string CSharp,
    string TypeName,
    string WireName,
    bool ExplicitName,
    ContractForm Form,
    string? Assembly,
    LocationModel? Location,
    bool IsValid,
    EquatableArray<CommandModel> Commands,
    EquatableArray<DtoModel> Dtos,
    EquatableArray<ErrorModel> Errors,
    EquatableArray<DiagnosticModel> Diagnostics);
