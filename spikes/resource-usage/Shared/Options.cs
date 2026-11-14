using System.Diagnostics;
using System.Globalization;

namespace Spar.ResourceUsage;

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

/// <summary>Log lines are <c>KEY k=v ...</c> on stdout, one per line, flushed at once.</summary>
internal static class Log
{
    public static void Line(string key, string text)
    {
        Console.Out.WriteLine($"{key} {text}");
        Console.Out.Flush();
    }

    public static double Ms(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    public static double Us(long started) => Stopwatch.GetElapsedTime(started).TotalMicroseconds;

    public static string Mb(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture);
}

/// <summary>Median, range and p90 of a set of timings.</summary>
internal static class Stats
{
    public static string Of(List<double> values, string unit = "us")
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var sorted = values.Order().ToArray();
        double At(double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Round(q * (sorted.Length - 1)))];
        return string.Create(CultureInfo.InvariantCulture,
            $"n={sorted.Length} median={At(0.5):F1}{unit} p90={At(0.9):F1}{unit} min={sorted[0]:F1}{unit} max={sorted[^1]:F1}{unit}");
    }

    /// <summary>Nanoseconds per call, from batches of 1,000 calls (Stopwatch ticks are 100 ns on Windows).</summary>
    public static string Batched(int batches, Action action)
    {
        action();
        var times = new List<double>(batches);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var b = 0; b < batches; b++)
        {
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < 1000; i++)
            {
                action();
            }

            times.Add(Stopwatch.GetElapsedTime(started).TotalNanoseconds / 1000);
        }

        var perCall = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (batches * 1000L);
        return Of(times, "ns") + $" allocB={perCall}";
    }

    /// <summary>Times <paramref name="action"/> <paramref name="count"/> times; also reports bytes allocated per call.</summary>
    public static string Time(int count, Action action, string unit = "us")
    {
        action();
        var times = new List<double>(count);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            var started = Stopwatch.GetTimestamp();
            action();
            var elapsed = Stopwatch.GetElapsedTime(started);
            times.Add(unit == "ms" ? elapsed.TotalMilliseconds : unit == "ns" ? elapsed.TotalNanoseconds : elapsed.TotalMicroseconds);
        }

        var perCall = (GC.GetAllocatedBytesForCurrentThread() - allocated) / count;
        return Of(times, unit) + $" allocB={perCall}";
    }
}
