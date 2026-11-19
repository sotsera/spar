using System.Diagnostics;

namespace Spar.WindowTests;

/// <summary>
/// Question 1, across processes: two test processes at once (<c>parallel</c>), and a test process killed mid-test
/// (<c>crash</c>), measuring when its leftover user-data folder stops being in use and can be deleted.
/// </summary>
internal static class Harness
{
    public static int Parallel(Options options)
    {
        var mode = options.Get("mode") ?? "inprivate";
        var count = options.Int("count", 25);
        var label = options.Get("label") ?? "par";
        Directory.CreateDirectory(Paths.Output);
        var children = new List<(Process Process, string LogPath)>();
        var extra = options.Get("child-args") ?? "";
        foreach (var name in new[] { "A", "B" })
        {
            var logPath = Path.Combine(Paths.Output, $"{label}-{mode}-{name}.log");
            var process = Start($"profiles --mode {mode} --count {count} --root {label}-{mode}-{name} {extra}", logPath);
            children.Add((process, logPath));
        }

        foreach (var (process, logPath) in children)
        {
            process.WaitForExit();
            Log.Write("CHILD", $"log={Path.GetFileName(logPath)} exit={process.ExitCode}");
            foreach (var line in ReadShared(logPath).Where(l => l.StartsWith("SUMMARY", StringComparison.Ordinal) || l.StartsWith("SHARED-END", StringComparison.Ordinal)))
            {
                Log.Write("CHILD-LINE", line);
            }
        }

        return 0;
    }

    /// <summary>
    /// Variants: <c>host</c> kills only the test process; <c>host-reputation</c> does the same with reputation checking
    /// on, so the orphaned browser lingers (~6.4 s, spikes/webview-profiles); <c>tree</c> kills the test process and
    /// every browser process of its folder at once, as a CI job timeout would.
    /// </summary>
    public static async Task<int> CrashAsync(Options options)
    {
        var variant = options.Get("variant") ?? "host";
        var runs = options.Int("runs", 5);
        Directory.CreateDirectory(Paths.Output);
        for (var run = 0; run < runs; run++)
        {
            var root = $"crash-{variant}-{run}";
            var logPath = Path.Combine(Paths.Output, $"{root}.log");
            var arguments = $"profiles --mode udf --count 5 --hang-at 2 --root {root}" + (variant == "host-reputation" ? " --reputation-on" : "");
            using var child = Start(arguments, logPath);
            var udf = await WaitForMidTestAsync(logPath, child, 60000);
            if (udf is null)
            {
                Log.Write("CRASH", $"variant={variant} run={run} error=no-midtest exit={(child.HasExited ? child.ExitCode : -1)}");
                if (!child.HasExited)
                {
                    child.Kill();
                }

                continue;
            }

            var marker = Paths.Marker(udf);
            var browsers = ProcessTools.Find(marker, keepHandles: false);
            var lockBefore = Native.LockState(udf);
            var killStart = Stopwatch.GetTimestamp();
            if (variant == "tree")
            {
                child.Kill();
                foreach (var browser in browsers)
                {
                    try
                    {
                        using var process = Process.GetProcessById(browser.Id);
                        process.Kill();
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }
            else
            {
                child.Kill();
            }

            child.WaitForExit();
            var killMs = Time.Ms(killStart);

            // Poll: browser processes of this folder (by command line), the lock file, and a delete attempt once both
            // say the folder is free. Every state change is logged with its time.
            double? goneMs = null, lockFreeMs = null, deletedMs = null;
            var lastLock = lockBefore;
            var transitions = new List<string> { $"0:lock={lockBefore}:processes={browsers.Count}" };
            var deleteAttempts = 0;
            var eager = options.Has("eager");
            while (Time.Ms(killStart) < 30000 && deletedMs is null)
            {
                var now = Time.Ms(killStart);
                var alive = ProcessTools.Find(marker, keepHandles: false).Count;
                var lockState = Native.LockState(udf);
                if (lockState != lastLock)
                {
                    transitions.Add($"{now:F0}:lock={lockState}:processes={alive}");
                    lastLock = lockState;
                }

                if (alive == 0 && goneMs is null)
                {
                    goneMs = now;
                    transitions.Add($"{now:F0}:processes=0:lock={lockState}");
                }

                if (lockState is "absent" or "free" && lockFreeMs is null)
                {
                    lockFreeMs = now;
                }

                if (eager || (goneMs is not null && lockFreeMs is not null))
                {
                    deleteAttempts++;
                    if (Paths.TryDelete(udf, out var error))
                    {
                        deletedMs = Time.Ms(killStart);
                    }
                    else if (deleteAttempts <= 3 || deleteAttempts % 50 == 0)
                    {
                        transitions.Add($"{now:F0}:delete-failed:{error}:processes={alive}:lock={lockState}");
                    }
                }

                await Task.Delay(5);
            }

            Log.Write("CRASH", $"variant={variant} run={run} browsersAtKill={browsers.Count} lockAtKill={lockBefore} killMs={killMs:F1} " +
                $"processesGoneMs={goneMs?.ToString("F0") ?? "never"} lockFreeMs={lockFreeMs?.ToString("F0") ?? "never"} " +
                $"deletedMs={deletedMs?.ToString("F0") ?? "never"} deleteAttempts={deleteAttempts} timeline=[{string.Join(' ', transitions)}]");
        }

        return 0;
    }

    /// <summary>
    /// The leftover check a next run would do: is any browser process of this folder alive (command line), and what
    /// does the lock file say. Run against a folder kept alive by another process to compare both signals.
    /// </summary>
    public static int Check(Options options)
    {
        var udf = Paths.Udf(options.Get("root")!);
        var started = Stopwatch.GetTimestamp();
        var processes = ProcessTools.Find(Paths.Marker(udf), keepHandles: false);
        var scanMs = Time.Ms(started);
        started = Stopwatch.GetTimestamp();
        var lockState = Native.LockState(udf);
        var lockMs = Time.Ms(started);
        Log.Write("CHECK", $"udf={udf} processes={processes.Count} scanMs={scanMs:F2} lock={lockState} lockMs={lockMs:F3}");
        return 0;
    }

    private static List<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static Process Start(string arguments, string logPath)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var process = Process.Start(info)!;
        var writer = new StreamWriter(logPath) { AutoFlush = true };
        var sync = new Lock();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (sync)
                {
                    writer.WriteLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (sync)
                {
                    writer.WriteLine("STDERR " + e.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<string?> WaitForMidTestAsync(string logPath, Process child, int timeoutMilliseconds)
    {
        var started = Stopwatch.GetTimestamp();
        while (Time.Ms(started) < timeoutMilliseconds && !child.HasExited)
        {
            try
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("MIDTEST", StringComparison.Ordinal))
                    {
                        return line[(line.IndexOf("udf=", StringComparison.Ordinal) + 4)..];
                    }
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(50);
        }

        return null;
    }
}
