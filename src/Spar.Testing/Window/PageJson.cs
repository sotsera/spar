using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// JSON for the page driver, written and read by hand (<see cref="Utf8JsonWriter"/>, <see cref="JsonDocument"/>), so it
/// needs no serializer metadata and stays NativeAOT-clean.
/// </summary>
internal static class PageJson
{
    /// <summary>A JSON object written by <paramref name="write"/>.</summary>
    public static string Object(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Parses <paramref name="json"/> and returns a copy of its root that outlives the document.</summary>
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>A string pattern, or a regular expression as <c>{ source, flags }</c> for the engine's <c>RegExp</c>.</summary>
    public static void WritePattern(Utf8JsonWriter writer, string name, string? text, Regex? regex)
    {
        if (regex is not null)
        {
            writer.WriteStartObject(name);
            writer.WriteString("source", regex.ToString());
            writer.WriteString("flags", Flags(regex.Options));
            writer.WriteEndObject();
            return;
        }

        if (text is not null)
        {
            writer.WriteString(name, text);
        }
    }

    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static int Int(JsonElement element, string name, int otherwise = 0) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : otherwise;

    public static double Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    public static IReadOnlyList<string> Strings(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())];
    }

    /// <summary>The flags of a JavaScript <c>RegExp</c> for .NET options; the pattern's syntax must be one both understand.</summary>
    private static string Flags(RegexOptions options)
    {
        var flags = new StringBuilder();
        if (options.HasFlag(RegexOptions.IgnoreCase))
        {
            flags.Append('i');
        }

        if (options.HasFlag(RegexOptions.Multiline))
        {
            flags.Append('m');
        }

        if (options.HasFlag(RegexOptions.Singleline))
        {
            flags.Append('s');
        }

        return flags.ToString();
    }
}
