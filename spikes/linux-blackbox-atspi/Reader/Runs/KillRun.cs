using System.Diagnostics;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.Runs;

/// <summary>
/// Question 5: does a launched app (and WebKit's processes under it) go when the test process is killed with SIGKILL?
/// <c>kill</c> starts a holder (this executable, verb <c>holder</c>) that launches the app the way a variant says, kills
/// the holder with SIGKILL after a delay, and counts what is left of the app's tree by a tag in its environment.
/// <c>dispose</c> takes the app down from a living test process (the fixture's dispose) in several ways.
/// </summary>
internal static class KillRun
{
    private static readonly string[] WebKitNames = ["WebKitWebProces", "WebKitNetworkP", "bwrap", "xdg-dbus-proxy", "BridgeProbe", "WebKitGPUProces"];

    /// <summary>kill &lt;variants&gt; &lt;delays&gt; &lt;runs&gt;; a delay is milliseconds after the launch, or <c>ready</c> (1 s after ui-ready).</summary>
    public static int Run(string[] args)
    {
        var variants = args[0].Split(',');
        var delays = args[1].Split(',');
        var runs = int.Parse(args[2]);
        Console.WriteLine($"unreadable environments of this user's WebKit-like processes before: {Unattributed()}");
        foreach (var variant in variants)
        {
            foreach (var delay in delays)
            {
                var left = new List<int>();
                var gone = new List<double>();
                var aliveAtKill = 0;
                for (var run = 0; run < runs; run++)
                {
                    var tag = $"{variant}-{delay}-{run}-{Environment.ProcessId}";
                    var info = new ProcessStartInfo(Environment.ProcessPath!, ["holder", variant, tag]) { RedirectStandardOutput = true, UseShellExecute = false };
                    using var holder = Process.Start(info)!;
                    var launched = holder.StandardOutput.ReadLine();
                    if (delay == "ready")
                    {
                        var line = holder.StandardOutput.ReadLine();
                        Thread.Sleep(1_000);
                        launched += " / " + line;
                    }
                    else
                    {
                        Thread.Sleep(int.Parse(delay));
                    }

                    var before = Census(tag);
                    aliveAtKill += before.Count > 0 ? 1 : 0;
                    Libc.kill(holder.Id, Libc.SigKill);
                    holder.WaitForExit();
                    var killed = Stopwatch.StartNew();
                    List<(int Pid, string Name)> after;
                    double? zero = null;
                    while (true)
                    {
                        after = Census(tag);
                        if (after.Count == 0)
                        {
                            zero = killed.Elapsed.TotalMilliseconds;
                            break;
                        }

                        if (killed.ElapsedMilliseconds > 3_000)
                        {
                            break;
                        }

                        Thread.Sleep(5);
                    }

                    left.Add(after.Count);
                    if (zero is { } z)
                    {
                        gone.Add(z);
                    }

                    Console.WriteLine($"{variant} delay {delay} run {run + 1}: [{launched}] at the kill {before.Count} [{Names(before)}]; " +
                        (zero is { } ms ? $"all gone {ms:0.0} ms after the holder died" : $"LEFT after 3 s: {after.Count} [{Names(after)}]"));
                    foreach (var (pid, _) in after)
                    {
                        // Ours: cleaned up so the next run starts clean.
                        Libc.kill(pid, Libc.SigKill);
                    }

                    Thread.Sleep(200);
                }

                Console.WriteLine($"SUMMARY kill {variant} delay {delay}: runs {runs}, app tree alive at the kill {aliveAtKill}, runs with processes left after 3 s {left.Count(l => l > 0)} (most {left.Max()}); " + Stats.Line("gone after", gone));
            }
        }

        Console.WriteLine($"unreadable environments of this user's WebKit-like processes after: {Unattributed()}");
        return 0;
    }

    /// <summary>The holder: launches the app as <paramref name="args"/>[0] says, prints its pid, then its ui-ready, then waits to be killed.</summary>
    public static int Holder(string[] args)
    {
        var variant = args[0];
        var extra = new Dictionary<string, string?> { ["SPIKE_TAG"] = args[1] };
        if (variant == "session")
        {
            // The fixture's whole set: a session bus of its own (KillOnParentExit), the accessibility bus and registry
            // activated through it, then the app (KillOnParentExit). The tag reaches the services through the
            // daemon's environment.
            var info = new ProcessStartInfo("dbus-daemon", ["--session", "--print-address=1", "--nofork", "--nopidfile"])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                KillOnParentExit = true,
            };
            info.Environment["SPIKE_TAG"] = args[1];
            var daemon = Process.Start(info)!;
            var address = daemon.StandardOutput.ReadLine()!.Trim();
            Libc.setenv("DBUS_SESSION_BUS_ADDRESS", address, 1);
            extra["DBUS_SESSION_BUS_ADDRESS"] = address;
            var bus = AtSpi.Bus.Connect();
            bus.RegistryApplications(out _);
            variant = "kpe";
        }

        AppRun? app = null;
        void Launch() => app = variant switch
        {
            "plain" or "plain-thread" => AppRun.Start(extra),
            "kpe" or "kpe-thread" => AppRun.Start(extra, killOnParentExit: true),
            "setpriv" or "setpriv-thread" => AppRun.Start(extra, "setpriv", ["--pdeathsig", "KILL", "--", AppRun.Probe]),
            _ => throw new ArgumentException($"no variant {variant}"),
        };
        if (variant.EndsWith("-thread", StringComparison.Ordinal))
        {
            // Launched from a thread that ends at once: a parent-death signal is tied to the thread that forked.
            var thread = new Thread(Launch);
            thread.Start();
            thread.Join();
        }
        else
        {
            Launch();
        }

        Console.WriteLine($"launched {app!.Id}");
        Console.Out.Flush();
        var ready = app.WaitReady();
        Console.WriteLine(ready is { } r ? $"ui-ready {r:0} ms" : $"no ui-ready (exited: {app.Process.HasExited}{(app.Process.HasExited ? ", code " + app.Process.ExitCode : "")})");
        Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// <summary>dispose &lt;modes&gt; &lt;runs&gt;: kill (the app only), tree (the app and its descendants), term (SIGTERM), subreaper (kill, as a subreaper).</summary>
    public static unsafe int Dispose(string[] args)
    {
        var modes = args[0].Split(',');
        var runs = int.Parse(args[1]);
        var printedTree = false;
        foreach (var mode in modes)
        {
            if (mode == "subreaper")
            {
                Console.WriteLine($"PR_SET_CHILD_SUBREAPER: {Libc.prctl(Libc.PrSetChildSubreaper, 1, 0, 0, 0)}");
            }

            var gone = new List<double>();
            var leftRuns = 0;
            for (var run = 0; run < runs; run++)
            {
                var tag = $"dispose-{mode}-{run}-{Environment.ProcessId}";
                using var app = AppRun.Start(new Dictionary<string, string?> { ["SPIKE_TAG"] = tag });
                app.WaitReady();
                Thread.Sleep(300);
                if (!printedTree)
                {
                    printedTree = true;
                    Console.WriteLine("the app's tree (pid ppid pgid sid name):");
                    foreach (var line in Tree(app.Id))
                    {
                        Console.WriteLine("  " + line);
                    }

                    var pidfd = Libc.syscall(Libc.SysPidfdOpen, app.Id, 0);
                    Console.WriteLine($"pidfd_open: {(pidfd >= 0 ? "fd " + pidfd : "errno " + System.Runtime.InteropServices.Marshal.GetLastPInvokeError())}");
                    if (pidfd >= 0)
                    {
                        Libc.close((int)pidfd);
                    }
                }

                var before = Census(tag);
                var sent = Stopwatch.StartNew();
                switch (mode)
                {
                    case "kill" or "subreaper":
                        app.Process.Kill();
                        break;
                    case "tree":
                        app.Process.Kill(entireProcessTree: true);
                        break;
                    case "term":
                        Libc.kill(app.Id, Libc.SigTerm);
                        break;
                }

                app.Process.WaitForExit(5_000);
                var exited = sent.Elapsed.TotalMilliseconds;
                List<(int Pid, string Name)> after;
                double? zero = null;
                var orphans = new List<string>();
                while (true)
                {
                    after = Census(tag);
                    if (mode == "subreaper" && orphans.Count == 0)
                    {
                        orphans = Tree(Environment.ProcessId).Skip(1).Where(l => l.Contains(" " + Environment.ProcessId + " ", StringComparison.Ordinal)).ToList();
                    }

                    if (after.Count == 0)
                    {
                        zero = sent.Elapsed.TotalMilliseconds;
                        break;
                    }

                    if (sent.ElapsedMilliseconds > 3_000)
                    {
                        break;
                    }

                    Thread.Sleep(2);
                }

                // Reap what was reparented to this process.
                int status;
                while (mode == "subreaper" && Libc.waitpid(-1, &status, 1) > 0)
                {
                }

                leftRuns += after.Count > 0 ? 1 : 0;
                if (zero is { } z)
                {
                    gone.Add(z);
                }

                Console.WriteLine($"dispose {mode} run {run + 1}: {before.Count} processes [{Names(before)}]; app exited {exited:0.0} ms, " +
                    (zero is { } ms ? $"all gone after {ms:0.0} ms" : $"LEFT after 3 s: {after.Count} [{Names(after)}]") +
                    (mode == "subreaper" ? $"; reparented to the test process: [{string.Join(", ", orphans)}]" : "") +
                    (mode == "term" ? $"; exit code {app.Process.ExitCode}" : ""));
                foreach (var (pid, _) in after)
                {
                    Libc.kill(pid, Libc.SigKill);
                }
            }

            Console.WriteLine($"SUMMARY dispose {mode}: runs {runs}, runs with processes left after 3 s {leftRuns}; " + Stats.Line("all gone after", gone));
        }

        return 0;
    }

    /// <summary>This user's processes whose environment carries <c>SPIKE_TAG=<paramref name="tag"/></c>.</summary>
    private static List<(int Pid, string Name)> Census(string tag)
    {
        var found = new List<(int, string)>();
        var marker = $"SPIKE_TAG={tag}\0";
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(folder), out var pid))
            {
                continue;
            }

            try
            {
                var environment = File.ReadAllText(Path.Combine(folder, "environ"));
                if (environment.Contains(marker, StringComparison.Ordinal))
                {
                    found.Add((pid, File.ReadAllText(Path.Combine(folder, "comm")).Trim()));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found;
    }

    /// <summary>How many of this user's WebKit-like processes have an environment that cannot be read (so no tag can be seen).</summary>
    private static int Unattributed()
    {
        var count = 0;
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            try
            {
                var name = File.ReadAllText(Path.Combine(folder, "comm")).Trim();
                if (WebKitNames.Any(n => name.StartsWith(n, StringComparison.Ordinal)))
                {
                    _ = File.ReadAllText(Path.Combine(folder, "environ"));
                }
            }
            catch (UnauthorizedAccessException)
            {
                count++;
            }
            catch (IOException)
            {
            }
        }

        return count;
    }

    /// <summary><paramref name="root"/> and its descendants: pid, ppid, process group, session, name.</summary>
    private static List<string> Tree(int root)
    {
        var all = new List<(int Pid, int Parent, int Group, int Session, string Name)>();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            try
            {
                var stat = File.ReadAllText(Path.Combine(folder, "stat"));
                var close = stat.LastIndexOf(')');
                var name = stat[(stat.IndexOf('(') + 1)..close];
                var fields = stat[(close + 2)..].Split(' ');
                all.Add((int.Parse(Path.GetFileName(folder)), int.Parse(fields[1]), int.Parse(fields[2]), int.Parse(fields[3]), name));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
            }
        }

        var lines = new List<string>();
        void Visit(int pid, int depth)
        {
            foreach (var p in all.Where(p => p.Pid == pid))
            {
                lines.Add($"{new string(' ', depth * 2)}{p.Pid} {p.Parent} {p.Group} {p.Session} {p.Name}");
            }

            foreach (var child in all.Where(p => p.Parent == pid))
            {
                Visit(child.Pid, depth + 1);
            }
        }

        Visit(root, 0);
        return lines;
    }

    private static string Names(List<(int Pid, string Name)> processes) =>
        string.Join(", ", processes.GroupBy(p => p.Name).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
}
