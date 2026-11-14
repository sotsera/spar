using System.Diagnostics;

namespace NativeServices.App;

/// <summary>Console reporting and timing helpers.</summary>
internal static class Report
{
    private static int s_passed;
    private static int s_failed;

    public static int Failed => s_failed;
    public static int Passed => s_passed;

    public static void Check(string name, bool ok, string detail)
    {
        if (ok)
        {
            s_passed++;
        }
        else
        {
            s_failed++;
        }

        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
    }

    public static void Info(string text) => Console.WriteLine($"       {text}");

    /// <summary>Times <paramref name="body"/> <paramref name="iterations"/> times and prints median, p95 and first.</summary>
    public static void Time(string name, int iterations, Action body)
    {
        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            body();
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Summary(name, samples);
    }

    public static void Summary(string name, double[] samples)
    {
        var first = samples[0];
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        var median = sorted[sorted.Length / 2];
        var p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
        Console.WriteLine(
            $"[TIME] {name}: n={samples.Length} first={first:F3}ms median={median:F3}ms p95={p95:F3}ms max={sorted[^1]:F3}ms");
    }
}
