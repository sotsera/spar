#if SPAR_DEVTOOLS
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace D8.App.Devtools;

internal static class DevtoolsEntry
{
    public static Task RunAsync(Host host) => host.Options.Mode switch
    {
        "selftest" => SelfTest.RunAsync(host),
#if SPAR_DEVTOOLS_SERVER
        "serve" => Server.RunAsync(host),
#endif
        _ => Task.CompletedTask,
    };
}

/// <summary>Measures each capability from inside the app and writes a JSON report.</summary>
internal static class SelfTest
{
    private const int TimeoutMilliseconds = 10_000;

    public static async Task RunAsync(Host host)
    {
        var automation = new Automation(host.WebView);
        var report = new Report();
        var n = host.Options.Iterations;
        var window = host.Options.Window.ToString();
        Directory.CreateDirectory(host.Options.OutputDirectory);

        try
        {
            // Readiness probe: two animation frames after navigation, i.e. the page has produced a frame.
            // --ready none skips it so the effect on the first trusted input can be compared.
            if (Environment.GetEnvironmentVariable("D8_READY") != "none")
            {
                await Measure(report, "ready.doubleRaf", 1, async _ => await automation.EvaluateAsync(
                    "new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(1))))",
                    awaitPromise: true));
            }

            await Measure(report, "evaluate.noop", n, async _ => await automation.EvaluateAsync("1"));

            // Tree: the first call is reported separately (domain warm-up).
            TreeResult? tree = null;
            await Measure(report, "tree.cold", 1, async _ => tree = await automation.TreeAsync());
            var cdp = new List<double>();
            var parse = new List<double>();
            await Measure(report, "tree", n, async _ =>
            {
                tree = await automation.TreeAsync();
                cdp.Add(tree.CdpMilliseconds);
                parse.Add(tree.ParseMilliseconds);
            });
            report.AddSeries("tree.cdpOnly", cdp);
            report.AddSeries("tree.parseOnly", parse);
            report.Facts["tree.rawChars"] = tree!.RawChars.ToString(CultureInfo.InvariantCulture);
            report.Facts["tree.summaryNodes"] = tree.Nodes.Count.ToString(CultureInfo.InvariantCulture);
            report.Facts["tree.summary"] = string.Join(" | ", tree.Nodes.Select(
                x => $"{x.Role}:{x.Name}{(x.Value is null ? "" : "=" + x.Value)}#{x.BackendNodeId}"));

            var textbox = Automation.Find(tree, "textbox", "Name");
            var button = Automation.Find(tree, "button", "Save");
            string? lastStatus = null;

            await MeasureFlow(report, "cdp", n, async i =>
            {
                var text = $"cdp-{i}";
                var fill = await Time(() => automation.FillWithInputAsync(textbox.BackendNodeId, text));
                var click = await Time(() => automation.ClickWithInputAsync(button.BackendNodeId));
                var wait = await Time(async () => lastStatus =
                    await automation.WaitForTextWithScriptAsync("status", $": {text} (", TimeoutMilliseconds));
                return (fill, click, wait);
            });
            report.Facts["cdp.lastStatus"] = lastStatus ?? "";

            await MeasureFlow(report, "script", n, async i =>
            {
                var text = $"script-{i}";
                var fill = await Time(() => automation.FillWithScriptAsync("name", text));
                var click = await Time(() => automation.ClickWithScriptAsync("save"));
                var wait = await Time(async () => lastStatus =
                    await automation.WaitForTextWithScriptAsync("status", $": {text} (", TimeoutMilliseconds));
                return (fill, click, wait);
            });
            report.Facts["script.lastStatus"] = lastStatus ?? "";

            var polls = new List<double>();
            await Measure(report, "waitFor.treePoll", Math.Max(1, n / 2), async i =>
            {
                var text = $"poll-{i}";
                await automation.FillWithInputAsync(textbox.BackendNodeId, text);
                await automation.ClickWithInputAsync(button.BackendNodeId);
                var (status, count) = await automation.WaitForTextWithTreeAsync($": {text} (", TimeoutMilliseconds);
                lastStatus = status;
                polls.Add(count);
            });
            report.AddSeries("waitFor.treePoll.polls", polls);

            byte[]? png = null;
            await Measure(report, "screenshot", n, async _ => png = await automation.ScreenshotAsync());
            if (png is not null)
            {
                var path = Path.Combine(host.Options.OutputDirectory, $"screenshot-{window}.png");
                await File.WriteAllBytesAsync(path, png);
                report.Facts["screenshot.bytes"] = png.Length.ToString(CultureInfo.InvariantCulture);
                report.Facts["screenshot.isPng"] = (png.Length > 8 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G')
                    .ToString();
                report.Facts["screenshot.path"] = path;
            }
        }
        catch (Exception exception)
        {
            report.Facts["failure"] = exception.GetType().Name + ": " + exception.Message;
            host.ExitCode = 1;
        }

        foreach (var (name, value) in host.StartupMilliseconds)
        {
            report.Facts["startup." + name + "Ms"] = value.ToString("F1", CultureInfo.InvariantCulture);
        }

        report.Facts["window"] = window;
        report.Facts["aot"] = (!RuntimeFeature.IsDynamicCodeCompiled).ToString();
        report.Facts["devtoolsUi"] = host.Options.DevtoolsUiEnabled.ToString();
        var build = RuntimeFeature.IsDynamicCodeCompiled ? "jit" : "aot";
        var suffix = host.Options.DevtoolsUiEnabled ? "" : "-devtoolsui-off";
        var output = Path.Combine(host.Options.OutputDirectory, $"selftest-{window}-{build}{suffix}.json");
        report.Write(output);
        report.Print();
        Console.WriteLine("Report: " + output);
        host.Close();
    }

    private static async Task Measure(Report report, string name, int count, Func<int, Task> action)
    {
        var samples = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                await WithTimeout(action(i), name);
            }
            catch (Exception exception)
            {
                report.Facts[name + ".error"] = exception.GetType().Name + ": " + exception.Message;
                report.AddSeries(name, samples);
                return;
            }

            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        report.AddSeries(name, samples);
    }

    private static async Task MeasureFlow(
        Report report, string name, int count, Func<int, Task<(double Fill, double Click, double Wait)>> flow)
    {
        var fill = new List<double>(count);
        var click = new List<double>(count);
        var wait = new List<double>(count);
        await Measure(report, name + ".total", count, async i =>
        {
            var result = await flow(i);
            fill.Add(result.Fill);
            click.Add(result.Click);
            wait.Add(result.Wait);
        });
        report.AddSeries(name + ".fill", fill);
        report.AddSeries(name + ".click", click);
        report.AddSeries(name + ".waitFor", wait);
    }

    private static async Task<double> Time(Func<Task> action)
    {
        var start = Stopwatch.GetTimestamp();
        await action();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static async Task WithTimeout(Task task, string name)
    {
        // A timeout only stops waiting; the CDP call may still complete later.
        var finished = await Task.WhenAny(task, Task.Delay(TimeoutMilliseconds));
        if (finished != task)
        {
            throw new TimeoutException($"{name} did not complete within {TimeoutMilliseconds} ms.");
        }

        await task;
    }
}

internal sealed class Report
{
    private readonly List<(string Name, double[] Samples)> _series = [];

    public SortedDictionary<string, string> Facts { get; } = new(StringComparer.Ordinal);

    public void AddSeries(string name, List<double> samples) => _series.Add((name, [.. samples]));

    public void Write(string path)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartObject("facts");
        foreach (var (key, value) in Facts)
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
        writer.WriteStartObject("series");
        foreach (var (name, samples) in _series)
        {
            var stats = Stats.Of(samples);
            writer.WriteStartObject(name);
            writer.WriteNumber("n", samples.Length);
            writer.WriteNumber("min", Math.Round(stats.Min, 3));
            writer.WriteNumber("median", Math.Round(stats.Median, 3));
            writer.WriteNumber("p95", Math.Round(stats.P95, 3));
            writer.WriteNumber("max", Math.Round(stats.Max, 3));
            writer.WriteStartArray("samples");
            foreach (var sample in samples)
            {
                writer.WriteNumberValue(Math.Round(sample, 3));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public void Print()
    {
        Console.WriteLine($"{"series",-28} {"n",4} {"min",9} {"median",9} {"p95",9} {"max",9}");
        foreach (var (name, samples) in _series)
        {
            var s = Stats.Of(samples);
            Console.WriteLine($"{name,-28} {samples.Length,4} {s.Min,9:F2} {s.Median,9:F2} {s.P95,9:F2} {s.Max,9:F2}");
        }

        foreach (var (key, value) in Facts)
        {
            Console.WriteLine($"{key} = {(value.Length > 400 ? value[..400] + "…" : value)}");
        }
    }
}

internal readonly record struct Stats(double Min, double Median, double P95, double Max)
{
    public static Stats Of(double[] samples)
    {
        if (samples.Length == 0)
        {
            return default;
        }

        var sorted = samples.Order().ToArray();
        return new Stats(sorted[0], Percentile(sorted, 0.5), Percentile(sorted, 0.95), sorted[^1]);
    }

    private static double Percentile(double[] sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
#endif
