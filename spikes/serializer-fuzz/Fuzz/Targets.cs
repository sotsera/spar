using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bench;
using ACodec = Spar.Generated.FuzzContractCodec;
using FCodec = Spar.Generated.Fixed.FuzzContractCodec;

namespace Fuzz;

internal delegate T SpanParser<T>(ReadOnlySpan<byte> payload);

/// <summary>Which deserializer: A original, F fixed copy, B the bench's System.Text.Json context, S the strict reference.</summary>
internal enum ReaderKind
{
    A,
    F,
    B,
    S,
}

/// <summary>Which serializer: A as shipped, A2 the tuned emitter on a reused-style writer, their fixed copies, and B.</summary>
internal enum WriterKind
{
    A,
    A2,
    F,
    F2,
    B,
}

/// <summary>A read or write result. <see cref="Bytes"/> is the canonical value (read) or the output (write).</summary>
internal readonly record struct Outcome(bool Ok, byte[]? Bytes, string Kind, string Message)
{
    public bool IsJsonException => !Ok && Kind == "JsonException";

    public string Key => Ok ? "ok" : $"{Kind}: {Message}";

    public static Outcome Success(byte[] bytes) => new(true, bytes, "ok", string.Empty);

    public static Outcome Failure(Exception ex) =>
        new(false, null, ex is JsonException ? "JsonException" : ex.GetType().Name, Text.Normalize(ex.Message));
}

internal abstract class Target(string name)
{
    public string Name { get; } = name;

    public abstract object? Generate(ValueGen gen);

    public abstract J ToTree(object? value);

    public abstract Outcome Read(ReaderKind kind, byte[] input);

    public abstract Outcome Write(WriterKind kind, object? value);

    /// <summary>The canonical bytes of a generated value, or null when even the canonical writer refuses it.</summary>
    public abstract byte[]? Canon(object? value);

    /// <summary>
    /// Reads <paramref name="written"/> and compares it with <paramref name="source"/> field by field (ordinal strings,
    /// double bit patterns). Unlike the canonical bytes, this sees changes a lossy writer would hide.
    /// </summary>
    public abstract bool ReadsBackExactly(ReaderKind kind, byte[] written, object? source);
}

internal sealed class Target<T>(
    string name,
    Func<ValueGen, T> generate,
    Func<T, J> toTree,
    SpanParser<T> readA,
    SpanParser<T> readF,
    Func<T, byte[]> writeA,
    Action<Utf8JsonWriter, T> writeA2,
    Func<T, byte[]> writeF,
    Action<Utf8JsonWriter, T> writeF2,
    Func<T, T, bool> exact) : Target(name)
{
    private readonly JsonTypeInfo<T> _web = (JsonTypeInfo<T>)WebContext.Default.Options.GetTypeInfo(typeof(T));
    private readonly JsonTypeInfo<T> _strict = (JsonTypeInfo<T>)FuzzContexts.Strict.GetTypeInfo(typeof(T));
    private readonly JsonTypeInfo<T> _canon = (JsonTypeInfo<T>)FuzzContexts.Canon.GetTypeInfo(typeof(T));

    public override object? Generate(ValueGen gen) => generate(gen);

    public override J ToTree(object? value) => toTree((T)value!);

    public override Outcome Read(ReaderKind kind, byte[] input)
    {
        T value;
        try
        {
            value = kind switch
            {
                ReaderKind.A => readA(input),
                ReaderKind.F => readF(input),
                ReaderKind.B => JsonSerializer.Deserialize(input, _web)!,
                _ => JsonSerializer.Deserialize(input, _strict)!,
            };
        }
        catch (Exception ex)
        {
            return Outcome.Failure(ex);
        }

        return Canon(value) is { } canon ? Outcome.Success(canon) : new Outcome(true, [], "ok", "canon failed");
    }

    public override Outcome Write(WriterKind kind, object? value)
    {
        var typed = (T)value!;
        try
        {
            return Outcome.Success(kind switch
            {
                WriterKind.A => writeA(typed),
                WriterKind.A2 => Tuned(writeA2, typed, new JsonWriterOptions { SkipValidation = true }),
                WriterKind.F => writeF(typed),
                WriterKind.F2 => Tuned(writeF2, typed, SparJsonFixed.ResponseWriterOptions),
                _ => JsonSerializer.SerializeToUtf8Bytes(typed, _web),
            });
        }
        catch (Exception ex)
        {
            return Outcome.Failure(ex);
        }
    }

    public override bool ReadsBackExactly(ReaderKind kind, byte[] written, object? source)
    {
        try
        {
            var value = kind switch
            {
                ReaderKind.A => readA(written),
                ReaderKind.F => readF(written),
                ReaderKind.B => JsonSerializer.Deserialize(written, _web)!,
                _ => JsonSerializer.Deserialize(written, _strict)!,
            };
            return exact(value, (T)source!);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public override byte[]? Canon(object? value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes((T)value!, _canon);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            // NaN, infinities, lone surrogates and over-deep values have no canonical form.
            return null;
        }
    }

    // The A2 recommendation: a reused writer with SkipValidation, one exact copy of the output.
    private static byte[] Tuned(Action<Utf8JsonWriter, T> write, T value, JsonWriterOptions options)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            write(writer, value);
        }

        return buffer.WrittenSpan.ToArray();
    }
}

internal static class Targets
{
    public static readonly Target Note = new Target<Note>(
        "Note", static g => g.Note(0), Trees.Note,
        static p => ACodec.ParseSaveAsync(p), static p => FCodec.ParseSaveAsync(p),
        ACodec.SerializeSaveAsync, ACodec.WriteTunedSaveAsync, FCodec.SerializeSaveAsync, FCodec.WriteTunedSaveAsync, Exact.Note);

    public static readonly Target Notes = new Target<Note[]>(
        "Note[]", static g => g.Notes(), Trees.Notes,
        static p => ACodec.ParseSaveManyAsync(p), static p => FCodec.ParseSaveManyAsync(p),
        ACodec.SerializeSaveManyAsync, ACodec.WriteTunedSaveManyAsync, FCodec.SerializeSaveManyAsync,
        FCodec.WriteTunedSaveManyAsync, Exact.Notes);

    public static readonly Target Wide = new Target<Wide>(
        "Wide", static g => g.Wide(0), Trees.Wide,
        static p => ACodec.ParseWideAsync(p), static p => FCodec.ParseWideAsync(p),
        ACodec.SerializeWideAsync, ACodec.WriteTunedWideAsync, FCodec.SerializeWideAsync, FCodec.WriteTunedWideAsync, Exact.Wide);

    public static readonly Target Int = new Target<int>(
        "int", static g => g.Int(), static v => Trees.Int(v),
        static p => ACodec.ParseCountAsync(p), static p => FCodec.ParseCountAsync(p),
        ACodec.SerializeCountAsync, ACodec.WriteTunedCountAsync, FCodec.SerializeCountAsync, FCodec.WriteTunedCountAsync, static (x, y) => x == y);

    public static readonly Target String = new Target<string?>(
        "string?", static g => g.MaybeNull(g.Str), Trees.MaybeString,
        static p => ACodec.ParseEchoAsync(p), static p => FCodec.ParseEchoAsync(p),
        ACodec.SerializeEchoAsync, ACodec.WriteTunedEchoAsync, FCodec.SerializeEchoAsync, FCodec.WriteTunedEchoAsync, static (x, y) => string.Equals(x, y, StringComparison.Ordinal));

    public static Target Pick(Random rng) => rng.Next(100) switch
    {
        < 40 => Note,
        < 55 => Notes,
        < 90 => Wide,
        < 95 => Int,
        _ => String,
    };
}

/// <summary>Exact structural equality: ordinal strings, double bit patterns, element by element.</summary>
internal static class Exact
{
    public static bool Note(Note? x, Note? y)
    {
        while (true)
        {
            if (x is null || y is null)
            {
                return x is null && y is null;
            }

            if (!Str(x.Text, y.Text) || !List(x.Tags, y.Tags, Str) || !Dbl(x.Score, y.Score) || x.Pinned != y.Pinned
                || x.Version != y.Version)
            {
                return false;
            }

            (x, y) = (x.Parent, y.Parent);
        }
    }

    public static bool Notes(Note[] x, Note[] y) => List(x, y, Note);

    public static bool Wide(Wide? x, Wide? y) => x is null || y is null ? x is null && y is null
        : Str(x.Name, y.Name) && Str(x.Nick, y.Nick) && x.Count == y.Count && x.Limit == y.Limit && x.Id == y.Id
            && x.Stamp == y.Stamp && Dbl(x.Ratio, y.Ratio) && NullableDbl(x.Weight, y.Weight) && x.On == y.On
            && x.Maybe == y.Maybe && List(x.Words, y.Words, Str) && List(x.Ints, y.Ints, (a, b) => a == b)
            && List(x.Stamps, y.Stamps, (a, b) => a == b) && List(x.Matrix, y.Matrix, (a, b) => List(a, b, Dbl))
            && NullableList(x.Labels, y.Labels, Str) && Note(x.Head, y.Head) && Note(x.Tail, y.Tail)
            && List(x.Items, y.Items, Note) && NullableList(x.Children, y.Children, Wide);

    private static bool Str(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

    private static bool Dbl(double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y);

    private static bool NullableDbl(double? x, double? y) => x is null || y is null ? x is null && y is null : Dbl(x.Value, y.Value);

    private static bool NullableList<T>(IReadOnlyList<T>? x, IReadOnlyList<T>? y, Func<T, T, bool> eq) =>
        x is null || y is null ? x is null && y is null : List(x, y, eq);

    private static bool List<T>(IReadOnlyList<T> x, IReadOnlyList<T> y, Func<T, T, bool> eq)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        for (var i = 0; i < x.Count; i++)
        {
            if (!eq(x[i], y[i]))
            {
                return false;
            }
        }

        return true;
    }
}
