using System.Diagnostics;
using System.Globalization;

namespace Spar.SingleInstance;

/// <summary>
/// One log file per process under the log folder. Times are QueryPerformanceCounter milliseconds, which are the same
/// clock in every process on the machine, so the primary can measure a handoff from the secondary's timestamps.
/// </summary>
internal static class Log
{
    private static readonly Lock s_gate = new();
    private static string? s_path;

    public static double Now => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public static long Ticks => Stopwatch.GetTimestamp();

    public static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static void Open(string? folder, string role)
    {
        if (folder is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        s_path = Path.Combine(folder, $"{role}-{Environment.ProcessId}.log");
    }

    public static void Mark(string text)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{Now:F3} pid={Environment.ProcessId} {text}");
        lock (s_gate)
        {
            Console.WriteLine(line);
            if (s_path is not null)
            {
                File.AppendAllText(s_path, line + "\n");
            }
        }
    }
}
