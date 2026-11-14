using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bridge;

/// <summary>
/// The payloads of the message mix, written with <see cref="Utf8JsonWriter"/> as generated code would. They are the
/// same bytes for every encoding.
/// </summary>
public static class Payloads
{
    public const string Fingerprint = "3d1faabdfca06185";
    public const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    public const string TraceState = "congo=t61rcWkgMzE,rojo=00f067aa0ba902b7";

    private static readonly JsonWriterOptions s_options = new() { SkipValidation = true };

    /// <summary>A small unary request, about 70 bytes: <c>{"title":"Buy milk","body":"…","tags":["home","shop"]}</c>.</summary>
    public static byte[] NoteRequest() => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("title"u8, "Buy milk");
        writer.WriteString("body"u8, "Two litres, semi-skimmed");
        writer.WriteStartArray("tags"u8);
        writer.WriteStringValue("home"u8);
        writer.WriteStringValue("shop"u8);
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    /// <summary>The unary result envelope for a saved note.</summary>
    public static byte[] NoteResult(long id) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ok"u8, true);
        writer.WriteStartObject("value"u8);
        writer.WriteString("id"u8, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("title"u8, "Buy milk");
        writer.WriteString("body"u8, "Two litres, semi-skimmed");
        writer.WriteString("createdAt"u8, "2026-11-14T10:20:30.1234567Z");
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    public static byte[] VoidResult() => "{\"ok\":true}"u8.ToArray();

    public static byte[] SerializationFailure() =>
        "{\"ok\":false,\"failure\":{\"kind\":\"response-serialization\",\"execution\":\"completed\"}}"u8.ToArray();

    /// <summary>Writes rows <paramref name="first"/> to <paramref name="first"/>+<paramref name="count"/>-1 as one array.</summary>
    public static void WriteRows(Utf8JsonWriter writer, long first, int count)
    {
        writer.WriteStartArray();
        for (var i = 0; i < count; i++)
        {
            WriteRow(writer, first + i);
        }

        writer.WriteEndArray();
    }

    /// <summary>One 10-column row of about 200 bytes, shaped like a SQL Server result row.</summary>
    public static void WriteRow(Utf8JsonWriter writer, long n)
    {
        writer.WriteStartObject();
        writer.WriteString("id"u8, (9_007_199_254_740_993L + n).ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("name"u8, "row number " + n);
        writer.WriteNumber("qty"u8, (int)(n % 1000));
        writer.WriteString("price"u8, ((n % 10_000) / 100m + 0.50m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteString("createdAt"u8, "2026-11-14T10:20:30.1234567");
        writer.WriteBoolean("active"u8, n % 3 == 0);
        writer.WriteString("code"u8, "AB-" + (n % 100_000));
        writer.WriteNumber("ratio"u8, (n % 97) / 97.0);
        writer.WriteString("note"u8, "lorem ipsum dolor sit amet");
        writer.WriteString("tag"u8, (n % 7) switch { 0 => "red", 1 => "green", 2 => "blue", _ => "none" });
        writer.WriteEndObject();
    }

    /// <summary>One push event, about 90 bytes; <c>at</c> is the backend's wall clock in Unix milliseconds.</summary>
    public static void WriteEvent(Utf8JsonWriter writer, long seq, double at)
    {
        writer.WriteStartObject();
        writer.WriteString("seq"u8, seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteNumber("at"u8, at);
        writer.WriteString("name"u8, "cpu.busy");
        writer.WriteNumber("value"u8, (seq % 1000) / 10.0);
        writer.WriteEndObject();
    }

    public static double NowUnixMs() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

    public static byte[] Rows(long first, int count) => Write(writer => WriteRows(writer, first, count));

    public static byte[] Events(long first, int count)
    {
        var at = NowUnixMs();
        return Write(writer =>
        {
            writer.WriteStartArray();
            for (var i = 0; i < count; i++)
            {
                WriteEvent(writer, first + i, at);
            }

            writer.WriteEndArray();
        });
    }

    /// <summary>Validates a JSON payload the way a generated reader must at least: every token, to the end.</summary>
    public static bool Validate(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 64 });
        try
        {
            while (reader.Read())
            {
            }

            return reader.BytesConsumed == json.Length;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static byte[] Write(Action<Utf8JsonWriter> write, JavaScriptEncoder? encoder = null)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, encoder is null ? s_options : new JsonWriterOptions { SkipValidation = true, Encoder = encoder }))
        {
            write(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
