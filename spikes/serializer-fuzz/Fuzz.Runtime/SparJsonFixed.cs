using System.Buffers;
using System.Text.Json;

namespace Bench;

/// <summary>
/// The fuzz spike's copy of <see cref="SparJson"/>, used by the fixed generator, with the fixes the fuzzer called
/// for. Each fix is marked FIX n and described as a generator requirement in RESULTS.md.
/// </summary>
public static class SparJsonFixed
{
    /// <summary>FIX 3: one depth limit for reading and writing, so everything written can be read back.</summary>
    public const int MaxDepth = 64;

    /// <summary>Options for a reused response writer (the A2 shape), with the shared depth limit.</summary>
    public static JsonWriterOptions ResponseWriterOptions => new() { SkipValidation = true, MaxDepth = MaxDepth };

    public static T[] ReadArray<T>(ref Utf8JsonReader reader, JsonValueReader<T> readElement)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw Unexpected(ref reader, "an array");
        }

        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(readElement(ref reader));
        }

        return [.. items];
    }

    /// <summary>FIX 2: invalid UTF-8 and unpaired surrogate escapes become JsonException.</summary>
    public static string ReadString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw Unexpected(ref reader, "a string");
        }

        try
        {
            return reader.GetString()!;
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException($"Invalid string at byte {reader.TokenStartIndex}: {ex.Message}", ex);
        }
    }

    /// <summary>FIX 2: a wrong token or an out-of-range number becomes JsonException.</summary>
    public static int ReadInt32(ref Utf8JsonReader reader) =>
        reader.TokenType != JsonTokenType.Number ? throw Unexpected(ref reader, "a number")
        : reader.TryGetInt32(out var value) ? value
        : throw OutOfRange(ref reader, "Int32");

    public static long ReadInt64(ref Utf8JsonReader reader) =>
        reader.TokenType != JsonTokenType.Number ? throw Unexpected(ref reader, "a number")
        : reader.TryGetInt64(out var value) ? value
        : throw OutOfRange(ref reader, "Int64");

    /// <summary>FIX 4: a literal that overflows to an infinity (1e999) is out of range, as it cannot be written back.</summary>
    public static double ReadDouble(ref Utf8JsonReader reader) =>
        reader.TokenType != JsonTokenType.Number ? throw Unexpected(ref reader, "a number")
        : reader.TryGetDouble(out var value) && double.IsFinite(value) ? value
        : throw OutOfRange(ref reader, "Double");

    public static bool ReadBoolean(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        _ => throw Unexpected(ref reader, "a boolean"),
    };

    public static byte[] Write<T>(T value, Action<Utf8JsonWriter, T> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaxDepth }))
        {
            write(writer, value);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static Utf8JsonReader Begin(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = MaxDepth });
        if (!reader.Read())
        {
            throw new JsonException("The payload is empty.");
        }

        return reader;
    }

    /// <summary>
    /// FIX 1: called after the top-level value. With the default options the reader itself throws a JsonException
    /// for any non-whitespace content after a complete value; the check below guards against a change of options.
    /// </summary>
    public static void End(ref Utf8JsonReader reader)
    {
        if (reader.Read())
        {
            throw new JsonException($"Unexpected {reader.TokenType} after the payload at byte {reader.TokenStartIndex}.");
        }
    }

    private static JsonException Unexpected(ref Utf8JsonReader reader, string expected) =>
        new($"Expected {expected} but found {reader.TokenType} at byte {reader.TokenStartIndex}.");

    private static JsonException OutOfRange(ref Utf8JsonReader reader, string type) =>
        new($"The number at byte {reader.TokenStartIndex} is not a valid {type}.");
}
