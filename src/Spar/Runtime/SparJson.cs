using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;
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

    private const string DateOnlyFormat = "yyyy-MM-dd";

    /// <summary><c>F</c> digits: up to seven on read, and trailing zeros left out on write.</summary>
    private const string TimeOnlyFormat = "HH:mm:ss.FFFFFFF";

    private static readonly SearchValues<byte> Base64Alphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/="u8);

    private static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = MaxDepth };
    private static readonly JsonWriterOptions WriterOptions = new() { MaxDepth = MaxDepth };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

    /// <summary>
    /// A string value. A lone-surrogate escape becomes U+FFFD, never an error, and invalid UTF-8 is a
    /// <see cref="JsonException"/> (wire rules); <c>GetString</c> throws <see cref="InvalidOperationException"/> for both.
    /// </summary>
    public static string ReadString(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.String);
        try
        {
            return reader.GetString()!;
        }
        catch (InvalidOperationException)
        {
            // The rare case, so the common one stays GetString's own fast path.
            return ReadUnpairedString(ref reader);
        }
    }

    private static string ReadUnpairedString(ref Utf8JsonReader reader)
    {
        var raw = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
        var text = new StringBuilder(raw.Length);
        var rest = raw;
        while (!rest.IsEmpty)
        {
            var backslash = rest.IndexOf((byte)'\\');
            var plain = backslash < 0 ? rest : rest[..backslash];
            AppendUtf8(text, plain, ref reader);
            if (backslash < 0)
            {
                break;
            }

            // The reader has already checked the escapes' syntax, so each is complete and its hex digits valid.
            var escape = rest[backslash + 1];
            if (escape == (byte)'u')
            {
                var code = rest.Slice(backslash + 2, 4);
                text.Append((char)int.Parse(Encoding.ASCII.GetString(code), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                rest = rest[(backslash + 6)..];
                continue;
            }

            text.Append(escape switch
            {
                (byte)'b' => '\b',
                (byte)'f' => '\f',
                (byte)'n' => '\n',
                (byte)'r' => '\r',
                (byte)'t' => '\t',
                _ => (char)escape, // '"', '\\' and '/'
            });
            rest = rest[(backslash + 2)..];
        }

        ReplaceUnpairedSurrogates(text);
        return text.ToString();
    }

    private static void AppendUtf8(StringBuilder text, ReadOnlySpan<byte> utf8, ref Utf8JsonReader reader)
    {
        try
        {
            text.Append(StrictUtf8.GetString(utf8));
        }
        catch (DecoderFallbackException)
        {
            throw Error(ref reader, "The string is not valid UTF-8");
        }
    }

    private static void ReplaceUnpairedSurrogates(StringBuilder text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(text[i]))
            {
                text[i] = '�';
            }
        }
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

    public static byte ReadByte(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.TryGetByte(out var value) ? value : throw Error(ref reader, "Expected an 8-bit unsigned integer");
    }

    public static sbyte ReadSByte(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.TryGetSByte(out var value) ? value : throw Error(ref reader, "Expected an 8-bit integer");
    }

    public static short ReadInt16(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.TryGetInt16(out var value) ? value : throw Error(ref reader, "Expected a 16-bit integer");
    }

    public static ushort ReadUInt16(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.TryGetUInt16(out var value) ? value : throw Error(ref reader, "Expected a 16-bit unsigned integer");
    }

    public static uint ReadUInt32(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.TryGetUInt32(out var value) ? value : throw Error(ref reader, "Expected a 32-bit unsigned integer");
    }

    /// <summary><c>ulong</c> travels as a canonical decimal string, like <c>long</c>.</summary>
    public static ulong ReadUInt64(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        var parsed = ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value);
        if (!parsed || text != value.ToString(CultureInfo.InvariantCulture))
        {
            throw Error(ref reader, "Expected a 64-bit unsigned integer as a canonical decimal string");
        }

        return value;
    }

    /// <summary>A number beyond <c>float</c>'s range parses as infinity rather than failing, so it is checked here.</summary>
    public static float ReadSingle(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        var read = reader.TryGetSingle(out var value);
        return read && float.IsFinite(value) ? value : throw Error(ref reader, "The number is not a finite float");
    }

    /// <summary>
    /// <c>decimal</c> travels as a canonical string that keeps its scale (<c>1.50</c>), with no exponent. Parsing rounds
    /// past 28 or 29 significant digits instead of failing, and accepts <c>+1</c> and <c>01</c>, so the text must be what
    /// the value writes back: nothing is rounded, and every value has one spelling.
    /// </summary>
    public static decimal ReadDecimal(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        var styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        var parsed = decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out var value);
        if (!parsed || text != value.ToString(CultureInfo.InvariantCulture))
        {
            throw Error(ref reader, "Expected a decimal as a canonical string, such as \"-12.50\"");
        }

        return value;
    }

    /// <summary>One UTF-16 code unit; a lone-surrogate escape is one U+FFFD, as in any string.</summary>
    public static char ReadChar(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        return text.Length == 1 ? text[0] : throw Error(ref reader, "Expected a string of one UTF-16 code unit");
    }

    /// <summary>ISO 8601 as System.Text.Json writes it: with <c>Z</c>, an offset, or nothing, by the value's Kind.</summary>
    public static DateTime ReadDateTime(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.String);
        try
        {
            return reader.TryGetDateTime(out var value) ? value : throw Error(ref reader, "Expected an ISO 8601 date and time");
        }
        catch (InvalidOperationException)
        {
            // A lone-surrogate escape: GetString's own failure, which the reader's date parse shares.
            throw Error(ref reader, "Expected an ISO 8601 date and time");
        }
    }

    public static DateOnly ReadDateOnly(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        var parsed = DateOnly.TryParseExact(text, DateOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value);
        return parsed ? value : throw Error(ref reader, "Expected a date as yyyy-MM-dd");
    }

    public static TimeOnly ReadTimeOnly(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        var parsed = TimeOnly.TryParseExact(text, TimeOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value);
        return parsed ? value : throw Error(ref reader, "Expected a time of day as HH:mm:ss with up to seven fraction digits");
    }

    /// <summary>The constant format, <c>[-][d.]hh:mm:ss[.fffffff]</c>, which parsing alone would accept as <c>1:2:3</c>.</summary>
    public static TimeSpan ReadTimeSpan(ref Utf8JsonReader reader)
    {
        var text = ReadString(ref reader);
        var parsed = TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var value);
        if (!parsed || text != value.ToString("c", CultureInfo.InvariantCulture))
        {
            throw Error(ref reader, "Expected a duration as [-][d.]hh:mm:ss[.fffffff]");
        }

        return value;
    }

    /// <summary>
    /// Padded base64 with no whitespace (wire rules). The reader's own decoder skips whitespace, so the raw text is
    /// checked first; an escape is refused too, since base64 never needs one.
    /// </summary>
    public static byte[] ReadBytes(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.String);
        var raw = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
        if (raw.IndexOfAnyExcept(Base64Alphabet) >= 0 || !reader.TryGetBytesFromBase64(out var value))
        {
            throw Error(ref reader, "Expected padded base64 with no whitespace");
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
        var text = ReadString(ref reader);
        if (!HasOffset(text) || !TryGetDateTimeOffset(ref reader, out var value))
        {
            throw Error(ref reader, "Expected an ISO 8601 date and time with an offset");
        }

        return value;
    }

    /// <summary>The reader's own ISO 8601 parse, which throws, rather than fails, on a lone-surrogate escape.</summary>
    private static bool TryGetDateTimeOffset(ref Utf8JsonReader reader, out DateTimeOffset value)
    {
        try
        {
            return reader.TryGetDateTimeOffset(out value);
        }
        catch (InvalidOperationException)
        {
            value = default;
            return false;
        }
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

    public static void WriteUInt64(Utf8JsonWriter writer, ulong value) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>The scale is kept and there is never an exponent; negative zero writes as zero.</summary>
    public static void WriteDecimal(Utf8JsonWriter writer, decimal value) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A lone surrogate becomes U+FFFD, as the writer does for any string.</summary>
    public static void WriteChar(Utf8JsonWriter writer, char value) => writer.WriteStringValue(new ReadOnlySpan<char>(in value));

    public static void WriteDateOnly(Utf8JsonWriter writer, DateOnly value) =>
        writer.WriteStringValue(value.ToString(DateOnlyFormat, CultureInfo.InvariantCulture));

    /// <summary>The fraction only as long as it needs to be, and none when it is zero.</summary>
    public static void WriteTimeOnly(Utf8JsonWriter writer, TimeOnly value) =>
        writer.WriteStringValue(value.ToString(TimeOnlyFormat, CultureInfo.InvariantCulture));

    public static void WriteTimeSpan(Utf8JsonWriter writer, TimeSpan value) =>
        writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));

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
