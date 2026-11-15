using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Spar.Runtime;

/// <summary>Reads one wire value; the reader is on the value's first token and is left on its last.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate T SparReader<out T>(ref Utf8JsonReader reader);

/// <summary>
/// The pieces generated serializers share: one depth limit for reading and writing, trailing content rejected, every
/// malformed input a <see cref="JsonException"/> with its byte offset, and the primitives of the wire-rules table
/// (plan section 2). No System.Text.Json metadata and no reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparJson
{
    public const int MaxDepth = 64;

    private static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = MaxDepth };
    private static readonly JsonWriterOptions WriterOptions = new() { MaxDepth = MaxDepth };

    /// <summary>Reads exactly one value from <paramref name="json"/>.</summary>
    public static T Read<T>(ReadOnlyMemory<byte> json, SparReader<T> read)
    {
        var reader = new Utf8JsonReader(json.Span, ReaderOptions);
        if (!reader.Read())
        {
            throw new JsonException("The JSON is empty.", null, null, 0);
        }

        var value = read(ref reader);
        if (reader.Read())
        {
            throw Error(ref reader, "Trailing content after the value");
        }

        return value;
    }

    /// <summary>Writes one value into its own buffer, so a failure leaves nothing half-sent.</summary>
    public static ReadOnlyMemory<byte> Write<T>(T value, Action<Utf8JsonWriter, T> write)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer, WriterOptions);
        write(writer, value);
        writer.Flush();
        return buffer.WrittenMemory;
    }

    public static JsonException Error(ref Utf8JsonReader reader, string message) =>
        new($"{message} at byte {reader.TokenStartIndex}.", null, null, reader.TokenStartIndex);

    public static void Expect(ref Utf8JsonReader reader, JsonTokenType type)
    {
        if (reader.TokenType != type)
        {
            throw Error(ref reader, $"Expected {type}, found {reader.TokenType}");
        }
    }

    /// <summary>Moves to the next property name, or returns false at the object's end.</summary>
    public static bool NextProperty(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.EndObject)
        {
            return false;
        }

        Expect(ref reader, JsonTokenType.PropertyName);
        return true;
    }

    /// <summary>A property declared twice is malformed (wire rules).</summary>
    public static void Duplicate(ref Utf8JsonReader reader) => throw Error(ref reader, "Property declared twice");

    /// <summary>A required property was missing from the object that just ended.</summary>
    public static JsonException Missing(ref Utf8JsonReader reader, string name) =>
        Error(ref reader, $"Required property '{name}' is missing in the object ending");

    /// <summary>Unknown properties are skipped without validation (wire rules).</summary>
    public static void SkipValue(ref Utf8JsonReader reader)
    {
        reader.Read();
        reader.Skip();
    }

    public static string ReadString(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.String);
        return reader.GetString()!;
    }

    public static bool ReadBoolean(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        _ => throw Error(ref reader, $"Expected a boolean, found {reader.TokenType}"),
    };

    public static int ReadInt32(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.TryGetInt32(out var value) ? value : throw Error(ref reader, "Expected a 32-bit integer");
    }

    public static double ReadDouble(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        var read = reader.TryGetDouble(out var value);
        return read && double.IsFinite(value) ? value : throw Error(ref reader, "The number is not a finite double");
    }

    /// <summary><c>long</c> travels as a canonical decimal string, because JSON numbers lose precision above 2^53.</summary>
    public static long ReadInt64(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        var parsed = long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value);
        if (!parsed || text != value.ToString(CultureInfo.InvariantCulture))
        {
            throw Error(ref reader, "Expected a 64-bit integer as a canonical decimal string");
        }

        return value;
    }

    public static Guid ReadGuid(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        return Guid.TryParseExact(text, "D", out var value) ? value : throw Error(ref reader, "Expected a GUID");
    }

    /// <summary>ISO 8601 with an offset, required on read (wire rules): without one the instant would depend on the reader's time zone.</summary>
    public static DateTimeOffset ReadDateTimeOffset(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.String);
        var text = reader.GetString()!;
        if (!HasOffset(text) || !reader.TryGetDateTimeOffset(out var value))
        {
            throw Error(ref reader, "Expected an ISO 8601 date and time with an offset");
        }

        return value;
    }

    private static bool HasOffset(string text) =>
        text.EndsWith('Z') || (text.Length > 6 && text[^6] is '+' or '-' && text[^3] == ':');

    public static IReadOnlyList<T> ReadList<T>(ref Utf8JsonReader reader, SparReader<T> readItem)
    {
        Expect(ref reader, JsonTokenType.StartArray);
        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(readItem(ref reader));
        }

        return items;
    }

    public static void WriteInt64(Utf8JsonWriter writer, long value) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    public static void WriteGuid(Utf8JsonWriter writer, Guid value) => writer.WriteStringValue(value.ToString("D"));

    public static void WriteList<T>(Utf8JsonWriter writer, IReadOnlyList<T> items, Action<Utf8JsonWriter, T> writeItem)
    {
        writer.WriteStartArray();
        foreach (var item in items)
        {
            writeItem(writer, item);
        }

        writer.WriteEndArray();
    }
}
