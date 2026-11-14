using System.Diagnostics;
using System.Globalization;

namespace Integ.Linux;

/// <summary>
/// Marks on standard output: "M &lt;monotonic ms&gt; &lt;ms since start&gt; text". Stopwatch is CLOCK_MONOTONIC on Linux, the
/// same clock as the X11 tool's and the fake tray host's timestamps, so logs from several processes can be merged.
/// </summary>
internal static class Log
{
    private static readonly long s_start = Stopwatch.GetTimestamp();
    private static readonly Lock s_gate = new();

    public static double Monotonic => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public static double Since => Stopwatch.GetElapsedTime(s_start).TotalMilliseconds;

    public static double StartMonotonic => s_start * 1000.0 / Stopwatch.Frequency;

    public static void Mark(string text)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"M {Monotonic:F3} {Since,9:F1} {text}");
        lock (s_gate)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    public static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
