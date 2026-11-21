using System.Collections.Concurrent;
using System.Diagnostics;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi;

/// <summary>
/// One launch of the app under test (the Production NativeAOT bridge probe): its process, its standard error lines with
/// their arrival times, and its log file's <c>ui-ready</c> line. The app inherits the run's environment (display, D-Bus
/// session, private HOME and XDG folders) plus <paramref name="extra"/>.
/// </summary>
internal sealed class AppRun : IDisposable
{
    private readonly ConcurrentQueue<(long At, string Line)> _errors = new();
    private readonly long _logStart;

    private AppRun(Process process, long started, long logStart)
    {
        Process = process;
        Started = started;
        _logStart = logStart;
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                _errors.Enqueue((Stopwatch.GetTimestamp(), line));
            }
        };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
    }

    public Process Process { get; }

    public int Id => Process.Id;

    /// <summary>When the launch began (<see cref="Stopwatch"/> ticks).</summary>
    public long Started { get; }

    public static string Probe => Environment.GetEnvironmentVariable("SPIKE_PROBE") ?? throw new InvalidOperationException("SPIKE_PROBE is not set");

    public static string? LogFile
    {
        get
        {
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME")!;
            return Directory.Exists(data) ? Directory.EnumerateFiles(data, "app.log", SearchOption.AllDirectories).FirstOrDefault() : null;
        }
    }

    public static AppRun Start(IReadOnlyDictionary<string, string?>? extra = null, string? file = null, IEnumerable<string>? arguments = null, bool killOnParentExit = false)
    {
        var info = new ProcessStartInfo(file ?? Probe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments ?? [])
        {
            info.ArgumentList.Add(argument);
        }

        if (extra is not null)
        {
            foreach (var (name, value) in extra)
            {
                if (value is null)
                {
                    info.Environment.Remove(name);
                }
                else
                {
                    info.Environment[name] = value;
                }
            }
        }

        if (killOnParentExit)
        {
            info.KillOnParentExit = true;
        }

        var logStart = LogFile is { } log ? new FileInfo(log).Length : 0;
        var started = Stopwatch.GetTimestamp();
        var process = Process.Start(info) ?? throw new InvalidOperationException("The app did not start.");
        return new AppRun(process, started, logStart);
    }

    /// <summary>Milliseconds since the launch.</summary>
    public double Since(long at) => (at - Started) * 1000.0 / Stopwatch.Frequency;

    public double Elapsed => Since(Stopwatch.GetTimestamp());

    public IReadOnlyList<(long At, string Line)> Errors => _errors.ToArray();

    /// <summary>The first standard error line containing <paramref name="text"/>, and when it arrived.</summary>
    public (long At, string Line)? ErrorLine(string text) =>
        _errors.FirstOrDefault(e => e.Line.Contains(text, StringComparison.Ordinal)) is { Line: not null } found ? found : null;

    /// <summary>The log's lines written since this launch (the log file is shared by instances of the same data root).</summary>
    public string[] LogLines()
    {
        if (LogFile is not { } log)
        {
            return [];
        }

        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(Math.Min(_logStart, stream.Length), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Waits for the log's <c>ui-ready</c> line; milliseconds since the launch when it was seen, or null.</summary>
    public double? WaitReady(int timeoutMs = 15_000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs && !Process.HasExited)
        {
            if (LogLines().Any(l => l.Contains(" ui-ready at ", StringComparison.Ordinal)))
            {
                return Elapsed;
            }

            Thread.Sleep(5);
        }

        return null;
    }

    /// <summary>Waits for the exit; its code and milliseconds since <paramref name="from"/>, or null on timeout.</summary>
    public (int Code, double Ms)? WaitExit(long from, int timeoutMs = 10_000)
    {
        if (!Process.WaitForExit(timeoutMs))
        {
            return null;
        }

        return (Process.ExitCode, (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency);
    }

    public void Signal(int signal) => Libc.kill(Id, signal);

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Libc.kill(Id, Libc.SigCont);
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        Process.Dispose();
    }
}

/// <summary>Percentiles and the evidence's output.</summary>
internal static class Stats
{
    public static string Line(string what, IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return $"{what}: no values";
        }

        var sorted = values.OrderBy(v => v).ToArray();
        double At(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
        return $"{what}: n={sorted.Length} p50={At(0.5):0.0} p95={At(0.95):0.0} min={sorted[0]:0.0} max={sorted[^1]:0.0} ms";
    }

    public static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
}
