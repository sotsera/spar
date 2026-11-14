using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Spar.WebViewProfiles;

/// <summary>
/// Linux counterpart of Harness: runs host processes of this executable and watches their child processes (WebKit's
/// web and network processes, and on 6.0 the sandbox's bwrap and xdg-dbus-proxy) from outside by polling /proc.
/// </summary>
internal static class LinuxHarness
{
    public static int Run(string verb, Options options)
    {
        Directory.CreateDirectory(Paths.Output);
        return verb switch
        {
            "shutdown" => Shutdown(options),
            "relaunch" => Relaunch(options),
            "concurrent" => Concurrent(options),
            "clean" => Clean(options.Get("windows") ?? throw new ArgumentException("--windows")) ? 0 : 1,
            _ => throw new ArgumentException("Unknown harness verb " + verb),
        };
    }

    private static string Root(string name) => Paths.Udf($"linux-{LinuxHost.Api}-{name}");

    public static bool Clean(string name)
    {
        var root = Root(name);
        var started = Stopwatch.GetTimestamp();
        while (Proc.FileUsers(root).Count > 0)
        {
            if (Stopwatch.GetElapsedTime(started).TotalSeconds > 20)
            {
                Console.WriteLine($"{root} is still in use; not deleting.");
                return false;
            }

            Thread.Sleep(20);
        }

        return Paths.TryDelete(root);
    }

    private static int Shutdown(Options options)
    {
        var variant = options.Get("variant") ?? "plain";
        var name = options.Get("windows") ?? "q1" + variant;
        var runs = options.Int("runs", 30);
        var kill = options.Has("kill");
        var linger = options.Int("linger", 0);
        var extra = options.Get("page-extra") ?? "";
        var hostArgs = Split(options.Get("host"));
        using var output = File.CreateText(Path.Combine(Paths.Output, $"linux-{LinuxHost.Api}-shutdown-{variant}-{Stamp()}.jsonl"));
        Clean(name);
        string? previous = null;
        var records = new List<JsonElement>();
        for (var i = 0; i < runs; i++)
        {
            var cleanMs = double.NaN;
            if (options.Has("fresh-udf") && i > 0)
            {
                var cleanStart = Stopwatch.GetTimestamp();
                Clean(name);
                cleanMs = Stopwatch.GetElapsedTime(cleanStart).TotalMilliseconds;
                previous = null;
            }

            var token = $"{variant}{i}";
            var launch = Stopwatch.GetTimestamp();
            using var host = HostProcess.Start(["--scenario", "shutdown", "--windows", name, "--handshake", "--page", $"op=readwrite&token={token}{extra}", .. hostArgs]);
            if (!host.WaitFor("READY", 60000))
            {
                Console.WriteLine($"run {i}: no READY: {host.Last("HOST-FAILURE")}");
                host.Kill();
                continue;
            }

            var children = Proc.Descendants(host.Pid);
            if (linger > 0)
            {
                Thread.Sleep(linger);
            }

            long closeAt;
            if (kill)
            {
                closeAt = LinuxHost.Now;
                host.Kill();
            }
            else
            {
                host.Send("go");
                host.WaitFor("CLOSE-START", 10000);
                closeAt = host.FileTime("CLOSE-START");
            }

            // Poll the host and every child every millisecond until all are gone (30 s at most).
            var gone = new Dictionary<int, double>();
            double hostExit = double.NaN;
            var deadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
            while (Stopwatch.GetTimestamp() < deadline && (gone.Count < children.Count || double.IsNaN(hostExit)))
            {
                var now = LinuxHost.Now;
                if (double.IsNaN(hostExit) && host.HasExited)
                {
                    hostExit = (now - closeAt) / 1e6;
                }

                foreach (var child in children)
                {
                    if (!gone.ContainsKey(child.Pid) && !Proc.Alive(child))
                    {
                        gone[child.Pid] = (now - closeAt) / 1e6;
                    }
                }

                Thread.Sleep(1);
            }

            host.WaitForExit(10000);
            var before = host.ResultBefore();
            var check = DataCheck(before, previous);
            var allMs = children.Count == gone.Count ? gone.Values.DefaultIfEmpty(0).Max() : double.PositiveInfinity;
            var exits = children.Select(c => (c.Name, Ms: gone.TryGetValue(c.Pid, out var ms) ? ms : double.PositiveInfinity)).ToList();
            var line = new StringBuilder();
            line.Append(CultureInfo.InvariantCulture, $"{{\"run\":{i},\"variant\":\"{variant}\",\"children\":{children.Count},\"hostExitMs\":{Json(hostExit)},\"allExitMs\":{Json(allMs)},");
            line.Append(CultureInfo.InvariantCulture, $"\"check\":\"{check}\",\"readyMs\":{host.ReadyMs:F0},\"cleanMs\":{Json(cleanMs)},\"launchToGoneMs\":{Stopwatch.GetElapsedTime(launch).TotalMilliseconds:F0},");
            line.Append(CultureInfo.InvariantCulture, $"\"exits\":[{string.Join(',', exits.Select(e => $"{{\"name\":\"{e.Name}\",\"ms\":{Json(e.Ms)}}}"))}],\"childrenExitedLog\":{Json(host.Number("CHILDREN-EXITED", "ms"))}}}");
            output.WriteLine(line.ToString());
            output.Flush();
            records.Add(JsonDocument.Parse(line.ToString()).RootElement.Clone());
            var closeEnd = host.Last("CLOSE-END ") is not null ? (host.FileTime("CLOSE-END") - closeAt) / 1e6 : double.NaN;
            var hostExitLog = host.Last("HOST-EXIT ") is not null ? (host.FileTime("HOST-EXIT") - closeAt) / 1e6 : double.NaN;
            Console.WriteLine($"run {i,2}: children={children.Count} all-exit={allMs,7:F0} ms host-exit={hostExit,6:F0} ms (log: close-end={closeEnd:F0} host-exit={hostExitLog:F0}) data={check} " +
                string.Join(' ', exits.Select(e => $"{e.Name}={e.Ms:F0}")));
            previous = token;
        }

        Summarize(variant, records);
        return 0;
    }

    private static string DataCheck(JsonElement? before, string? previous)
    {
        if (previous is null)
        {
            return "first";
        }

        if (before is not { } state)
        {
            return "no-result";
        }

        var missing = new List<string>();
        if (Text(state, "ls") != previous) missing.Add("localStorage");
        if (Text(state, "idb") != previous) missing.Add("indexedDB");
        return missing.Count == 0 ? "ok" : "LOST:" + string.Join('+', missing);
    }

    private static void Summarize(string variant, List<JsonElement> records)
    {
        double[] Values(string name) => records.Select(r => r.GetProperty(name)).Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).Order().ToArray();
        Console.WriteLine($"SUMMARY api={LinuxHost.Api} variant={variant} runs={records.Count}");
        Console.WriteLine($"  all children exited: {Stats(Values("allExitMs"))}; not exited in 30 s: {records.Count(r => r.GetProperty("allExitMs").ValueKind != JsonValueKind.Number)}");
        Console.WriteLine($"  host exited after close: {Stats(Values("hostExitMs"))}");
        Console.WriteLine($"  launch to READY: {Stats(Values("readyMs"))}; launch to all gone: {Stats(Values("launchToGoneMs"))}; clean before run: {Stats(Values("cleanMs"))}");
        var byName = records.SelectMany(r => r.GetProperty("exits").EnumerateArray())
            .GroupBy(e => e.GetProperty("name").GetString()!)
            .Select(g => $"{g.Key}: {Stats(g.Select(e => e.GetProperty("ms")).Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).Order().ToArray())}");
        foreach (var entry in byName)
        {
            Console.WriteLine("  " + entry);
        }

        Console.WriteLine($"  data: {string.Join(' ', records.GroupBy(r => r.GetProperty("check").GetString()).Select(g => $"{g.Key}={g.Count()}"))}");
    }

    private static int Relaunch(Options options)
    {
        var name = options.Get("windows") ?? "q2relaunch";
        var runs = options.Int("runs", 30);
        var hostArgs = Split(options.Get("host"));
        Clean(name);
        string? previous = null;
        List<Proc.Child> previousChildren = [];
        int ok = 0, dataOk = 0, overlap = 0;
        var ready = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var alive = previousChildren.Count(Proc.Alive);
            if (alive > 0) overlap++;
            var token = $"r{i}";
            using var host = HostProcess.Start(["--scenario", "shutdown", "--windows", name, "--handshake", "--page", $"op=readwrite&token={token}", .. hostArgs]);
            var isReady = host.WaitFor("READY", 60000);
            var children = isReady ? Proc.Descendants(host.Pid) : [];
            if (isReady)
            {
                ok++;
                ready.Add(host.ReadyMs);
                host.Send("go");
            }

            host.WaitForExit(30000);
            var before = host.ResultBefore();
            var good = previous is null || (before is { } b && Text(b, "ls") == previous && Text(b, "idb") == previous);
            if (good && previous is not null && isReady) dataOk++;
            Console.WriteLine($"run {i,2}: previous-children-alive-at-start={alive} ready={isReady} ready-ms={host.ReadyMs:F0} data={good} {(isReady ? "" : host.Last("HOST-FAILURE"))}");
            previous = isReady ? token : previous;
            previousChildren = children;
        }

        Console.WriteLine($"SUMMARY relaunch api={LinuxHost.Api} runs={runs} ready={ok} started-while-previous-children-alive={overlap} data-ok={dataOk}/{runs - 1}");
        Console.WriteLine($"  launch to READY: {Stats([.. ready.Order()])}");
        return 0;
    }

    private static int Concurrent(Options options)
    {
        // A writes A1; B starts on the same folders, reads, writes B1 and exits; A reads again; then C reads after both.
        var name = options.Get("windows") ?? "q2conc";
        var hostArgs = Split(options.Get("host"));
        Clean(name);
        using var a = HostProcess.Start(["--scenario", "shutdown", "--windows", name, "--handshake", "--page", "op=readwrite&token=A1", .. hostArgs]);
        if (!a.WaitFor("READY", 60000))
        {
            Console.WriteLine("A failed: " + a.Last("HOST-FAILURE"));
            return 1;
        }

        using var b = HostProcess.Start(["--scenario", "shutdown", "--windows", name, "--handshake", "--page", "op=readwrite&token=B1", .. hostArgs]);
        var bReady = b.WaitFor("READY", 60000);
        Console.WriteLine($"B ready={bReady} B saw: {Brief(b.ResultBefore())}");
        b.Send("go");
        b.WaitForExit(30000);
        a.Send("nav op=read");
        a.WaitFor("NAV-DONE", 30000);
        Console.WriteLine($"A after B exited saw: {Brief(a.ResultBefore())}");
        a.Send("go");
        a.WaitForExit(30000);
        using var c = HostProcess.Start(["--scenario", "shutdown", "--windows", name, "--handshake", "--page", "op=read", .. hostArgs]);
        c.WaitFor("READY", 60000);
        Console.WriteLine($"C after both saw: {Brief(c.ResultBefore())}");
        c.Send("go");
        c.WaitForExit(30000);
        var errors = a.Errors.Concat(b.Errors).Concat(c.Errors).Where(l => l.Length > 0).Distinct().Take(15).ToList();
        Console.WriteLine($"stderr lines (A+B+C, distinct, first 15): {errors.Count}");
        foreach (var error in errors)
        {
            Console.WriteLine("  " + (error.Length > 220 ? error[..220] : error));
        }

        return 0;
    }

    private static string Brief(JsonElement? state) => state is { } s
        ? $"ls={Text(s, "ls") ?? "null"} idb={Text(s, "idb") ?? "null"} cookie=[{Text(s, "cookie")}] cache={Text(s, "cacheStorage") ?? "null"}"
        : "no result";

    private static string[] Split(string? args) => args is null ? [] : args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Json(double value) => double.IsFinite(value) ? value.ToString("F1", CultureInfo.InvariantCulture) : "null";

    private static string Stats(double[] sorted)
    {
        if (sorted.Length == 0)
        {
            return "n=0";
        }

        double At(double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(q * (sorted.Length - 1) + 0.5))];
        return $"n={sorted.Length} min={sorted[0]:F0} p50={At(0.5):F0} p90={At(0.9):F0} max={sorted[^1]:F0} ms";
    }

    private sealed class HostProcess : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _lines = [];
        private readonly List<string> _errors = [];
        private readonly long _startedAt = LinuxHost.Now;

        private HostProcess(Process process)
        {
            _process = process;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (_lines)
                    {
                        _lines.Add(e.Data);
                        Monitor.PulseAll(_lines);
                    }
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (_errors)
                    {
                        _errors.Add(e.Data);
                    }
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public int Pid => _process.Id;

        public bool HasExited => _process.HasExited;

        public List<string> Errors
        {
            get
            {
                lock (_errors)
                {
                    return [.. _errors];
                }
            }
        }

        public double ReadyMs => Last("READY ") is not null ? (FileTime("READY") - _startedAt) / 1e6 : double.NaN;

        public static HostProcess Start(string[] args)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("host");
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            return new HostProcess(Process.Start(info)!);
        }

        public bool WaitFor(string key, int timeoutMilliseconds)
        {
            var deadline = Stopwatch.GetTimestamp() + (long)timeoutMilliseconds * Stopwatch.Frequency / 1000;
            var seen = 0;
            lock (_lines)
            {
                seen = key == "NAV-DONE" ? _lines.Count(l => l.StartsWith(key + " ", StringComparison.Ordinal)) : 0;
            }

            lock (_lines)
            {
                while (true)
                {
                    var count = _lines.Count(l => l.StartsWith(key + " ", StringComparison.Ordinal));
                    if (count > seen || (key != "NAV-DONE" && count > 0))
                    {
                        return true;
                    }

                    if (_lines.Any(l => l.StartsWith("HOST-EXIT ", StringComparison.Ordinal)) || Stopwatch.GetTimestamp() > deadline)
                    {
                        return false;
                    }

                    Monitor.Wait(_lines, 20);
                }
            }
        }

        public void Send(string line)
        {
            try
            {
                _process.StandardInput.WriteLine(line);
                _process.StandardInput.Flush();
            }
            catch (IOException)
            {
            }
        }

        public void WaitForExit(int timeoutMilliseconds)
        {
            if (_process.WaitForExit(timeoutMilliseconds))
            {
                _process.WaitForExit();
            }
        }

        public void Kill()
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }

        public string? Last(string key)
        {
            lock (_lines)
            {
                return _lines.LastOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
            }
        }

        public long FileTime(string key) => long.Parse(Field(Last(key + " ")!, "ft")!, CultureInfo.InvariantCulture);

        public double Number(string key, string field) =>
            Last(key) is { } line && Field(line, field) is { } value ? double.Parse(value, CultureInfo.InvariantCulture) : double.NaN;

        public JsonElement? ResultBefore()
        {
            if (Last("RESULT ") is not { } line)
            {
                return null;
            }

            using var document = JsonDocument.Parse(line[(line.IndexOf("json=", StringComparison.Ordinal) + 5)..]);
            return document.RootElement.TryGetProperty("before", out var before) ? before.Clone() : null;
        }

        public void Dispose() => _process.Dispose();

        private static string? Field(string line, string name)
        {
            foreach (var part in line.Split(' '))
            {
                if (part.StartsWith(name + "=", StringComparison.Ordinal))
                {
                    return part[(name.Length + 1)..];
                }
            }

            return null;
        }
    }
}
