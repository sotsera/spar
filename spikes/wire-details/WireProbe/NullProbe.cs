using System.Text.Json;

namespace WireProbe;

/// <summary>Item 4: top-level null and the success envelope for void, nullable and Result returns.</summary>
internal static class NullProbe
{
    public static void Run()
    {
        Console.WriteLine("### Reader on top-level payloads");
        foreach (var json in new[] { "null", "", "   ", "{}" })
        {
            Probe.Line($"Utf8JsonReader \"{json}\"", Probe.Try(() =>
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), Probe.ReaderOptions);
                return reader.Read() ? reader.TokenType.ToString() : "Read() = false";
            }));
            Probe.Line($"STJ Deserialize<string> \"{json}\"", Probe.Try(() => JsonSerializer.Deserialize(json, DefaultContext.Default.String)));
        }

        Console.WriteLine("### Envelope prototype (what the generated dispatch would write)");
        var file = new ResultFile("null.json");
        Add(file, "voidOk", w => { });
        Add(file, "nullableNull", w => w.WriteNull("value"));
        Add(file, "nullableValue", w => w.WriteString("value", "x"));
        Add(file, "resultNullableNull", w => w.WriteNull("value"));
        Add(file, "resultError", w =>
        {
            w.WriteStartObject("error");
            w.WriteString("code", "notFound");
            w.WriteEndObject();
        }, ok: false);
        file.Save();
        Console.WriteLine("- wrote results/null.json");
    }

    private static void Add(ResultFile file, string name, Action<Utf8JsonWriter> body, bool ok = true)
    {
        var bytes = Probe.WriteBytes(w =>
        {
            w.WriteStartObject();
            w.WriteNumber("id", 7);
            w.WriteBoolean("ok", ok);
            body(w);
            w.WriteEndObject();
        });
        Probe.Line(name, Probe.Show(bytes));
        file.AddRaw(name, bytes);
    }
}
