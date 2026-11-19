using System.Globalization;
using System.Text.Json;

namespace Spar.Testing;

/// <summary>How a guest test ended, as its test executable reported it.</summary>
internal enum GuestOutcome
{
    Passed,
    Failed,
    Skipped,
    NotRun,

    /// <summary>It started and never reported an end: the run was ended at its deadline, or the process died.</summary>
    Unfinished,
}

/// <summary>One test of a guest run: its display name, its outcome, why, and how long it took.</summary>
internal sealed record GuestTestResult(string DisplayName, GuestOutcome Outcome, string? Message, TimeSpan Duration, string? Output);

/// <summary>
/// Reads an xUnit v3 test executable's JSON reporter (<c>-reporter json</c>): one JSON message per line, written as each
/// test starts and ends, so a run ended at its deadline still has the results of every test that finished. Only the
/// test messages are read: <c>test-starting</c> carries the display name, then <c>test-passed</c>, <c>test-failed</c>,
/// <c>test-skipped</c> or <c>test-not-run</c> by the test's id, and <c>test-output</c> its output. A line cut off by the
/// end of the file is ignored.
/// </summary>
internal static class GuestTestResults
{
    private const string Unfinished = "It started and reported no end: the run was ended or its process died.";

    public static IReadOnlyList<GuestTestResult> Read(string path) =>
        File.Exists(path) ? Parse(ReadShared(path)) : [];

    internal static IReadOnlyList<GuestTestResult> Parse(string text)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var ended = new Dictionary<string, GuestTestResult>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (Message(line) is not { } message)
            {
                continue;
            }

            using (message)
            {
                var root = message.RootElement;
                var id = Text(root, "TestUniqueID");
                if (id is null)
                {
                    continue;
                }

                switch (Text(root, "$type"))
                {
                    case "test-starting":
                        names[id] = Text(root, "TestDisplayName") ?? id;
                        order.Add(id);
                        break;
                    case "test-output":
                        outputs[id] = (outputs.GetValueOrDefault(id) ?? "") + Text(root, "Output");
                        break;
                    case "test-passed":
                        ended[id] = Result(names, id, GuestOutcome.Passed, null, root);
                        break;
                    case "test-failed":
                        ended[id] = Result(names, id, GuestOutcome.Failed, Failure(root), root);
                        break;
                    case "test-skipped":
                        ended[id] = Result(names, id, GuestOutcome.Skipped, Text(root, "Reason"), root);
                        break;
                    case "test-not-run":
                        ended[id] = Result(names, id, GuestOutcome.NotRun, null, root);
                        break;
                }
            }
        }

        var results = new List<GuestTestResult>(order.Count);
        foreach (var id in order)
        {
            var result = ended.TryGetValue(id, out var end)
                ? end
                : new GuestTestResult(names[id], GuestOutcome.Unfinished, Unfinished, TimeSpan.Zero, null);
            results.Add(result with { Output = outputs.GetValueOrDefault(id) });
        }

        return results;
    }

    private static GuestTestResult Result(Dictionary<string, string> names, string id, GuestOutcome outcome, string? message, JsonElement root)
    {
        var seconds = root.TryGetProperty("ExecutionTime", out var time) && time.TryGetDecimal(out var value) ? (double)value : 0;
        return new GuestTestResult(names.GetValueOrDefault(id) ?? id, outcome, message, TimeSpan.FromSeconds(seconds), null);
    }

    /// <summary>A failure's exception types, messages and stack traces, outermost first.</summary>
    private static string Failure(JsonElement root)
    {
        var types = Strings(root, "ExceptionTypes");
        var messages = Strings(root, "Messages");
        var traces = Strings(root, "StackTraces");
        var parts = new List<string>();
        for (var index = 0; index < Math.Max(types.Count, messages.Count); index++)
        {
            var type = index < types.Count ? types[index] : "";
            var text = index < messages.Count ? messages[index] : "";
            var trace = index < traces.Count && !string.IsNullOrEmpty(traces[index]) ? "\n" + traces[index] : "";
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{type}: {text}{trace}"));
        }

        return string.Join("\n---- ", parts);
    }

    private static List<string> Strings(JsonElement root, string name) =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "")]
            : [];

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The line as a JSON object, or null for anything else (an empty line, a line cut off, other output).</summary>
    private static JsonDocument? Message(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a file the guest may still be writing.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
