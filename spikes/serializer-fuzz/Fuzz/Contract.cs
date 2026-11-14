using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Bench;

/// <summary>The D1 spike's DTO, unchanged from serializer-bench.</summary>
public sealed record Note(string Text, IReadOnlyList<string> Tags, Note? Parent, double Score, bool Pinned, long? Version);

/// <summary>Every wire kind the generator supports, nullable and not, in properties, arrays and nested arrays.</summary>
public sealed record Wide(
    string Name,
    string? Nick,
    int Count,
    int? Limit,
    long Id,
    long? Stamp,
    double Ratio,
    double? Weight,
    bool On,
    bool? Maybe,
    string[] Words,
    IReadOnlyList<int> Ints,
    long?[] Stamps,
    double[][] Matrix,
    IReadOnlyList<string?>? Labels,
    Note Head,
    Note? Tail,
    Note[] Items,
    IReadOnlyList<Wide?>? Children);

/// <summary>Makes both generators emit (A) codecs for every fuzz target type.</summary>
[SparContract("fuzz")]
public sealed class FuzzContract
{
    [SparCommand]
    public Task<Note> SaveAsync(Note note) => Task.FromResult(note);

    [SparCommand]
    public Task<Note[]> SaveManyAsync(Note[] notes) => Task.FromResult(notes);

    [SparCommand]
    public Task<Wide> WideAsync(Wide wide) => Task.FromResult(wide);

    [SparCommand]
    public Task<int> CountAsync(int count) => Task.FromResult(count);

    [SparCommand]
    public Task<string?> EchoAsync(string? text) => Task.FromResult(text);
}

/// <summary>(B) The serializer-bench baseline options (D1): Web defaults plus the two Respect* switches.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(Note[]))]
[JsonSerializable(typeof(Wide))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal sealed partial class WebContext : JsonSerializerContext;

/// <summary>
/// (S) A reference aligned with A's intended contract: case-sensitive camelCase names, no numbers in strings,
/// nulls rejected where the annotation forbids them; <see cref="FuzzContexts.Strict"/> also makes nullable
/// constructor parameters optional. Divergences from S that are not divergences from B are contract choices.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.General,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(Note[]))]
[JsonSerializable(typeof(Wide))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal sealed partial class StrictContext : JsonSerializerContext;

internal static class FuzzContexts
{
    /// <summary>Strict reference options: <see cref="StrictContext"/> with nullable parameters made optional.</summary>
    public static readonly JsonSerializerOptions Strict = new(StrictContext.Default.Options)
    {
        TypeInfoResolver = StrictContext.Default.WithAddedModifier(static info =>
        {
            foreach (var property in info.Properties)
            {
                if (property.AssociatedParameter is { IsNullable: true })
                {
                    property.IsRequired = false;
                }
            }
        }),
    };

    /// <summary>Canonical form for value comparison: B's writer with a depth limit above every generated value.</summary>
    public static readonly JsonSerializerOptions Canon = new(StrictContext.Default.Options) { MaxDepth = 1100 };
}
