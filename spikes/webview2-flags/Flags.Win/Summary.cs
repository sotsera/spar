using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace WebView2Flags;

/// <summary>Per-variant medians and ranges of a plan's runs.</summary>
internal static class Summary
{
    public static string Write(string suite, JsonArray variants, List<JsonObject> runs)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"suite {suite}: {runs.Count} runs");
        foreach (var v in variants)
        {
            var name = (string)v!["name"]!;
            var mine = runs.Where(r => (string?)r["job"]!["variant"] == name).ToList();
            var ok = mine.Where(r => r["error"] is null).ToList();
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"## {name}  (args: {v["args"] ?? "none"}; wasmEval {v["wasmEval"] ?? false}; reputation {v["reputation"] ?? false})");
            text.AppendLine(CultureInfo.InvariantCulture, $"runs {mine.Count}, errors {mine.Count - ok.Count}, runtime {string.Join(",", ok.Select(r => (string?)r["runtime"]).Distinct())}");
            foreach (var bad in mine.Where(r => r["error"] is not null).Take(3))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  error: {((string)bad["error"]!).Split('\n')[0]}");
            }

            text.AppendLine(Stat("environment ms", ok.Select(r => (double?)r["envMs"])));
            text.AppendLine(Stat("controller ms", ok.Select(r => (double?)r["controllerMs"])));
            text.AppendLine(Stat("browser exit after close ms", ok.Select(r => (double?)r["browserExitMs"])));
            var navs = ok.Where(r => r["navigations"] is JsonArray a && a.Count > 0 && a[0] is JsonObject).Select(r => r["navigations"]!.AsArray()).ToList();
            if (navs.Count > 0)
            {
                text.AppendLine(Stat("first Navigate -> first script (boot.js) ms", navs.Select(a => (double?)a[0]!["bootMs"])));
                text.AppendLine(Stat("first Navigate -> ready ms", navs.Select(a => (double?)a[0]!["readyMs"])));
                text.AppendLine(Stat("later Navigate -> ready ms", navs.SelectMany(a => a.Skip(1)).Select(n => (double?)n!["readyMs"])));
                var flags = navs.Select(a => a[^1]!["page"]).Select(p => $"WebAssembly={p!["wasm"]} showOpenFilePicker={p["picker"]} gc={p["gc"]}").Distinct();
                text.AppendLine("page globals: " + string.Join(" | ", flags));
            }

            if (ok.Any(r => r["loopback"] is not null))
            {
                var counts = ok.Select(r => (double?)r["loopback"]!.AsArray().Count).ToList();
                text.AppendLine(Stat("loopback connections per run (this run's processes)", counts) + $", total {counts.Sum()}");
                var kinds = ok.SelectMany(r => r["loopback"]!.AsArray()).GroupBy(c => $"{c!["local"]} {c["first"]}").Select(g => $"{g.Key} x{g.Count()}");
                text.AppendLine("  " + string.Join("; ", kinds));
                text.AppendLine(CultureInfo.InvariantCulture, $"  other processes' connections during these runs: {ok.Sum(r => (int?)r["loopbackOthers"] ?? 0)}");
            }

            foreach (var line in ok.Where(r => r["wasm"] is not null).Select(r => Wasm(r["wasm"]!)).GroupBy(s => s))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"wasm ({line.Count()} runs): {line.Key}");
            }

            var commandLines = ok.Select(r => (string?)r["browserCommandLine"]).Where(c => c is not null).Select(Interesting).Distinct().ToList();
            foreach (var c in commandLines)
            {
                text.AppendLine("browser switches: " + c);
            }

            var rendererLines = ok.SelectMany(r => r["processes"]?.AsArray() ?? []).Where(p => (string?)p!["kind"] == "Renderer").Select(p => Interesting((string?)p!["commandLine"])).Distinct();
            foreach (var c in rendererLines)
            {
                text.AppendLine("renderer switches: " + c);
            }
        }

        return text.ToString();
    }

    private static string Wasm(JsonNode w)
    {
        var parts = new List<string>();
        foreach (var context in new[] { "page", "dedicated", "shared", "service", "frame" })
        {
            var r = w[context];
            parts.Add(r is null ? $"{context}: missing" : r["error"] is { } e ? $"{context}: {e}" : $"{context}: {r["wasm"]}/{r["instantiate"]}/gc {r["gc"]}");
        }

        return string.Join("; ", parts) + $"; picker {w["picker"]}";
    }

    /// <summary>The switches of the browser's command line that the spike passes or that matter here.</summary>
    public static string Interesting(string? commandLine)
    {
        var keep = new[] { "--host-resolver-rules", "--js-flags", "--disable-blink-features", "--log-net-log", "--net-log-capture-mode", "--disable-features", "--enable-features" };
        var tokens = Tokens(commandLine ?? "");
        return string.Join(" ", tokens.Where(t => keep.Any(k => t.StartsWith(k, StringComparison.Ordinal))));
    }

    /// <summary>Splits a command line at spaces outside double quotes, keeping the quotes.</summary>
    private static List<string> Tokens(string s)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in s)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }

            if (ch == ' ' && !quoted)
            {
                if (current.Length > 0)
                {
                    list.Add(current.ToString());
                }

                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            list.Add(current.ToString());
        }

        return list;
    }

    public static string Stat(string label, IEnumerable<double?> values)
    {
        var v = values.Where(x => x is not null && !double.IsNaN(x.Value)).Select(x => x!.Value).Order().ToList();
        if (v.Count == 0)
        {
            return $"{label}: n/a";
        }

        var median = v.Count % 2 == 1 ? v[v.Count / 2] : (v[(v.Count / 2) - 1] + v[v.Count / 2]) / 2;
        return string.Create(CultureInfo.InvariantCulture, $"{label}: median {median:F1} (range {v[0]:F1}-{v[^1]:F1}, n {v.Count})");
    }
}
