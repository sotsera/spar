using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WireProbe;

public sealed record Row(string Name, int Qty, double Price, bool Active, string? Note, long Version);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Row))]
internal sealed partial class RowContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowDuplicateProperties = false)]
[JsonSerializable(typeof(Row))]
internal sealed partial class StrictRowContext : JsonSerializerContext;

/// <summary>Item 5: duplicate properties. What each System.Text.Json reader does, and the cost of rejecting them.</summary>
internal static class DuplicateProbe
{
    private const string Valid = """{"name":"widget","qty":3,"price":2.5,"active":true,"note":null,"version":42}""";

    public static void Run()
    {
        string[] inputs =
        [
            """{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"qty":2}""",
            "{\"name\":\"a\",\"qty\":1,\"price\":1,\"active\":true,\"note\":null,\"version\":1,\"q" + "\\" + "u0074y\":2}",
            """{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"Qty":2}""",
            """{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"extra":1,"extra":2}""",
        ];
        foreach (var json in inputs)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);
            Console.WriteLine($"Input `{json}`");
            Probe.Line("Utf8JsonReader tokens", Probe.Try(() =>
            {
                var reader = new Utf8JsonReader(utf8, Probe.ReaderOptions);
                var names = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        names.Add(reader.GetString()!);
                    }
                }

                return string.Join(",", names);
            }));
            Probe.Line("JsonDocument default, GetProperty(\"qty\")", Probe.Try(() =>
            {
                using var doc = JsonDocument.Parse(utf8);
                return doc.RootElement.GetProperty("qty").GetRawText();
            }));
            Probe.Line("JsonDocument AllowDuplicateProperties=false", Probe.Try(() =>
            {
                using var doc = JsonDocument.Parse(utf8, new JsonDocumentOptions { AllowDuplicateProperties = false });
                return doc.RootElement.GetProperty("qty").GetRawText();
            }));
            Probe.Line("JsonNode.Parse default, [\"qty\"]", Probe.Try(() => JsonNode.Parse(utf8)!["qty"]!.ToJsonString()));
            Probe.Line("JsonSerializer default", Probe.Try(() => JsonSerializer.Deserialize(utf8, RowContext.Default.Row)!.Qty));
            Probe.Line("JsonSerializer AllowDuplicateProperties=false",
                Probe.Try(() => JsonSerializer.Deserialize(utf8, StrictRowContext.Default.Row)!.Qty));
            Probe.Line("generated-style reader, last wins", Probe.Try(() => Parse(utf8, rejectDuplicates: false).Qty));
            Probe.Line("generated-style reader, reject", Probe.Try(() => Parse(utf8, rejectDuplicates: true).Qty));
        }

        Benchmark();
    }

    private static readonly JsonEncodedText s_name = JsonEncodedText.Encode("name");
    private static readonly JsonEncodedText s_qty = JsonEncodedText.Encode("qty");
    private static readonly JsonEncodedText s_price = JsonEncodedText.Encode("price");
    private static readonly JsonEncodedText s_active = JsonEncodedText.Encode("active");
    private static readonly JsonEncodedText s_note = JsonEncodedText.Encode("note");
    private static readonly JsonEncodedText s_version = JsonEncodedText.Encode("version");

    /// <summary>The shape a generated reader takes; a bit per declared property records that it was seen.</summary>
    public static Row Parse(ReadOnlySpan<byte> utf8, bool rejectDuplicates)
    {
        var reader = new Utf8JsonReader(utf8, Probe.ReaderOptions);
        reader.Read();
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object at byte {reader.TokenStartIndex}.");
        }

        string? name = null;
        int qty = 0;
        double price = 0;
        bool active = false;
        string? note = null;
        long version = 0;
        var seen = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            int bit;
            var at = reader.TokenStartIndex;
            if (reader.ValueTextEquals(s_name.EncodedUtf8Bytes))
            {
                bit = 1;
                reader.Read();
                name = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_qty.EncodedUtf8Bytes))
            {
                bit = 2;
                reader.Read();
                qty = reader.GetInt32();
            }
            else if (reader.ValueTextEquals(s_price.EncodedUtf8Bytes))
            {
                bit = 4;
                reader.Read();
                price = reader.GetDouble();
            }
            else if (reader.ValueTextEquals(s_active.EncodedUtf8Bytes))
            {
                bit = 8;
                reader.Read();
                active = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals(s_note.EncodedUtf8Bytes))
            {
                bit = 16;
                reader.Read();
                note = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_version.EncodedUtf8Bytes))
            {
                bit = 32;
                reader.Read();
                version = reader.GetInt64();
            }
            else
            {
                reader.Read();
                reader.Skip();
                continue;
            }

            if (rejectDuplicates)
            {
                if ((seen & bit) != 0)
                {
                    throw new JsonException($"Duplicate property at byte {at}.");
                }

                seen |= bit;
            }
        }

        if (reader.Read())
        {
            throw new JsonException($"Trailing content at byte {reader.TokenStartIndex}.");
        }

        return new Row(name ?? throw new JsonException("Missing name."), qty, price, active, note, version);
    }

    private static void Benchmark()
    {
        const int count = 1_000_000;
        var utf8 = Encoding.UTF8.GetBytes(Valid);
        long total = 0;
        var lastWins = Probe.Time(() =>
        {
            for (var i = 0; i < count; i++)
            {
                total += Parse(utf8, rejectDuplicates: false).Qty;
            }
        });
        var reject = Probe.Time(() =>
        {
            for (var i = 0; i < count; i++)
            {
                total += Parse(utf8, rejectDuplicates: true).Qty;
            }
        });
        var stjDefault = Probe.Time(() =>
        {
            for (var i = 0; i < count; i++)
            {
                total += JsonSerializer.Deserialize(utf8, RowContext.Default.Row)!.Qty;
            }
        });
        var stjStrict = Probe.Time(() =>
        {
            for (var i = 0; i < count; i++)
            {
                total += JsonSerializer.Deserialize(utf8, StrictRowContext.Default.Row)!.Qty;
            }
        });
        Console.WriteLine($"### Cost, {count:N0} parses of a {utf8.Length}-byte, 6-property object (min of 5 runs)");
        Probe.Line("generated-style, last wins", $"{lastWins} ms");
        Probe.Line("generated-style, reject (bit mask)", $"{reject} ms");
        Probe.Line("STJ source-generated, default", $"{stjDefault} ms");
        Probe.Line("STJ source-generated, AllowDuplicateProperties=false", $"{stjStrict} ms ({total % 3})");
    }
}
