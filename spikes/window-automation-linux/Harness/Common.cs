using System.Diagnostics;
using System.Globalization;

namespace Spar.WindowAutomation.Linux;

internal static class Log
{
    private static readonly Lock s_lock = new();

    public static void Write(string key, string text)
    {
        lock (s_lock)
        {
            Console.Out.WriteLine($"{key} {text}");
            Console.Out.Flush();
        }
    }
}

internal static class Stats
{
    public static string Summary(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var sorted = values.OrderBy(v => v).ToArray();
        return string.Create(CultureInfo.InvariantCulture,
            $"n={sorted.Length} min={sorted[0]:F1} p50={Percentile(sorted, 0.50):F1} p95={Percentile(sorted, 0.95):F1} max={sorted[^1]:F1}");
    }

    // Nearest-rank percentile.
    public static double Percentile(double[] sorted, double fraction)
    {
        var rank = (int)Math.Ceiling(fraction * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>Per-step timings, collected over many runs.</summary>
internal sealed class Timings
{
    private readonly Dictionary<string, List<double>> _steps = [];
    private readonly List<string> _order = [];

    public async Task<T> TimeAsync<T>(string step, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await action();
        Add(step, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    public async Task TimeAsync(string step, Func<Task> action)
    {
        var started = Stopwatch.GetTimestamp();
        await action();
        Add(step, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public void Add(string step, double milliseconds)
    {
        lock (_steps)
        {
            if (!_steps.TryGetValue(step, out var list))
            {
                _steps[step] = list = [];
                _order.Add(step);
            }

            list.Add(milliseconds);
        }
    }

    public void Report(string prefix)
    {
        foreach (var step in _order)
        {
            Log.Write(prefix, $"{step,-40} {Stats.Summary(_steps[step])}");
        }
    }
}

/// <summary><c>--name value</c> pairs; a name followed by another <c>--name</c> (or nothing) is a flag.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = [];

    public Options(IEnumerable<string> args)
    {
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = list[i][2..];
            _values[name] = i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal) ? list[++i] : "";
        }
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public int Int(string name, int fallback) => Get(name) is { Length: > 0 } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
}
