using System.Globalization;
using System.Text.Json;

namespace Spar.Tool.Devtools;

/// <summary>The devtools' answers as text: short lines a person or an agent reads at a glance.</summary>
internal static class DevtoolsOutput
{
    public static void Write(TextWriter output, string tool, JsonElement result)
    {
        switch (tool)
        {
            case "tree":
                Tree(output, result);
                break;
            case "click" or "fill":
                Action(output, tool, result);
                break;
            case "wait":
                output.WriteLine($"{Text(result, "element")} is {Text(result, "state")} ({Shown(result, "milliseconds")} ms)");
                break;
            case "contracts":
                Contracts(output, result);
                break;
            case "call":
                Call(output, result);
                break;
            case "calls":
                foreach (var call in Array(result, "calls"))
                {
                    WriteCall(output, call, "");
                }

                break;
            case "logs":
                foreach (var entry in Array(result, "entries"))
                {
                    var time = DateTimeOffset.TryParse(Text(entry, "time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                        ? at.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        : Text(entry, "time");
                    output.WriteLine($"{time} {Text(entry, "level"),-5} {Text(entry, "source"),-7} {Text(entry, "category")}: {Text(entry, "message")}");
                }

                break;
            default:
                foreach (var property in result.EnumerateObject())
                {
                    output.WriteLine($"{property.Name}: {(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText())}");
                }

                break;
        }
    }

    /// <summary>The PNG written to <paramref name="file"/> (or the project's obj/spar-devtools/), then its path.</summary>
    public static int Screenshot(TextWriter output, JsonElement result, string? file, string projectDirectory, bool json)
    {
        var path = Path.GetFullPath(file ?? Path.Combine(
            projectDirectory, "obj", "spar-devtools", $"screenshot-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, result.GetProperty("data").GetBytesFromBase64());
        var size = $"{Shown(result, "width")}x{Shown(result, "height")}";
        output.WriteLine(json
            ? $$"""{"path":{{JsonSerializerQuote(path)}},"width":{{Shown(result, "width")}},"height":{{Shown(result, "height")}},"bytes":{{Shown(result, "bytes")}}}"""
            : $"{path} ({size}, {Shown(result, "bytes")} bytes)");
        return ExitCodes.Success;
    }

    /// <summary>The elements an ambiguous target matched, under the refusal.</summary>
    public static void Matches(TextWriter error, JsonElement refused)
    {
        if (refused.TryGetProperty("data", out var data) && data.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
        {
            foreach (var match in matches.EnumerateArray())
            {
                error.WriteLine($"  {match.GetString()}");
            }
        }
    }

    internal static void Tree(TextWriter output, JsonElement result)
    {
        output.WriteLine($"{Text(result, "title")} — {Text(result, "url")} (document {Shown(result, "document")})");
        foreach (var node in Array(result, "nodes"))
        {
            var indent = new string(' ', 2 * (int)Number(node, "depth"));
            var name = Text(node, "name") is { Length: > 0 } named ? $" \"{named}\"" : "";
            var text = Text(node, "text") is { Length: > 0 } shown ? $": \"{shown}\"" : "";
            var value = Text(node, "value") is { Length: > 0 } given ? $" = \"{OneLine(given)}\"" : "";
            var states = new List<string>();
            foreach (var state in new[] { "checked", "disabled", "expanded", "selected", "pressed", "focused" })
            {
                if (node.TryGetProperty(state, out var flag))
                {
                    states.Add(flag.ValueKind == JsonValueKind.True ? state : "not " + state);
                }
            }

            if (node.TryGetProperty("level", out var level))
            {
                states.Add($"level {level.GetRawText()}");
            }

            var suffix = states.Count > 0 ? $" [{string.Join(", ", states)}]" : "";
            output.WriteLine($"{indent}{Text(node, "ref")} {Text(node, "role")}{name}{text}{value}{suffix}");
        }

        if (result.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True)
        {
            output.WriteLine("… more elements than the limit; spar tree --limit N shows more.");
        }
    }

    private static void Action(TextWriter output, string tool, JsonElement result)
    {
        var how = result.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True
            ? " (the page's own events: no trusted input on this desktop)"
            : "";
        output.WriteLine($"{(tool == "click" ? "clicked" : "filled")} {Text(result, "element")}{how}");
        var calls = Array(result, "calls").ToList();
        output.WriteLine(calls.Count == 0 ? "no calls while it ran" : $"calls while it ran ({calls.Count}):");
        foreach (var call in calls)
        {
            WriteCall(output, call, "  ");
        }
    }

    private static void Contracts(TextWriter output, JsonElement result)
    {
        output.WriteLine($"fingerprint {Text(result, "fingerprint")}");
        foreach (var command in Array(result, "commands"))
        {
            var limit = command.TryGetProperty("timeoutSeconds", out var seconds) ? $" (limit {seconds.GetRawText()} s)" : "";
            output.WriteLine($"  {Text(command, "name")}{limit}");
        }

        if (Text(result, "snapshot") is { Length: > 0 } snapshot)
        {
            output.WriteLine();
            output.Write(snapshot);
        }
    }

    private static void Call(TextWriter output, JsonElement result)
    {
        var outcome = Text(result, "outcome");
        output.WriteLine($"{Text(result, "command")}: {outcome}");
        var payload = outcome switch
        {
            "success" => result.TryGetProperty("value", out var value) ? value : default,
            "error" => result.TryGetProperty("error", out var declared) ? declared : default,
            _ => result.TryGetProperty("failure", out var failure) ? failure : default,
        };
        if (payload.ValueKind != JsonValueKind.Undefined)
        {
            output.WriteLine(JsonText.Indented(payload));
        }
    }

    private static void WriteCall(TextWriter output, JsonElement call, string indent)
    {
        var code = Text(call, "code") is { Length: > 0 } given ? $" {given}" : "";
        output.WriteLine($"{indent}#{Shown(call, "sequence")} {Text(call, "caller"),-8} {Text(call, "command")} {Text(call, "outcome")}{code} {Shown(call, "milliseconds")} ms");
    }

    /// <summary>A field's text on one line of the tree: its line breaks as <c>\n</c>.</summary>
    private static string OneLine(string text) =>
        text.Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static IEnumerable<JsonElement> Array(JsonElement result, string name) =>
        result.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /// <summary>A number as the output shows it, the same on every machine (4.8, not a culture's 4,8).</summary>
    private static string Shown(JsonElement element, string name) =>
        Number(element, name).ToString("0.###", CultureInfo.InvariantCulture);

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static string JsonSerializerQuote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
