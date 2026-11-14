using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Spar.WebViewProfiles;

/// <summary>
/// Runs host processes of this same executable and measures them from outside: kernel exit times of every browser
/// process of the UDF (matched by command line), relaunch behaviour, and concurrent instances. Raw records go to
/// artifacts/webview-profiles/*.jsonl; a summary goes to stdout.
/// </summary>
internal static class Harness
{
    public static int Run(string verb, Options options)
    {
        Directory.CreateDirectory(Paths.Output);
        return verb switch
        {
            "shutdown" => Shutdown(options),
            "relaunch" => Relaunch(options),
            "concurrent" => Concurrent(options),
            "clean" => Clean(options.Get("udf") ?? throw new ArgumentException("--udf")) ? 0 : 1,
            _ => throw new ArgumentException("Unknown harness verb " + verb),
        };
    }

    // ---- Q1: exit at once after Close, measure every browser process ------------------------------------------------

    private static int Shutdown(Options options)
    {
        var variant = options.Get("variant") ?? "plain";
        var udf = options.Get("udf") ?? "shutdown-" + variant;
        var runs = options.Int("runs", 30);
        var linger = options.Int("linger", 0);
        var kill = options.Has("kill");
        var extra = options.Get("page-extra") ?? "";
        var hostArgs = Split(options.Get("host"));
        var records = new List<JsonElement>();
        using var output = File.CreateText(Path.Combine(Paths.Output, $"shutdown-{variant}-{Stamp()}.jsonl"));
        if (!Clean(udf))
        {
            return 1;
        }

        string? previousToken = null;
        long previousStream = -1;
        for (var i = 0; i < runs; i++)
        {
            var token = $"{variant}{i}";
            var cleanMs = double.NaN;
            if (options.Has("fresh-udf") && i > 0)
            {
                // Per-test fresh UDF: wait for every process of the previous run, then delete the folder.
                var cleanStart = Stopwatch.GetTimestamp();
                Clean(udf);
                cleanMs = Stopwatch.GetElapsedTime(cleanStart).TotalMilliseconds;
                previousToken = null;
            }

            var launchStart = Stopwatch.GetTimestamp();
            using var host = HostProcess.Start(["--scenario", "shutdown", "--udf", udf, "--handshake", "--page", $"op=readwrite&token={token}{extra}", .. hostArgs]);
            if (!host.WaitFor("READY", 60000))
            {
                Console.WriteLine($"run {i}: no READY; exit={host.ExitCodeText}; last={host.Last("HOST-FAILURE")}");
                host.Kill();
                Clean(udf);
                continue;
            }

            var processes = ProcessTools.Find(Paths.Marker(udf), keepHandles: true);
            if (linger > 0)
            {
                Thread.Sleep(linger);
            }

            long closeAt;
            if (kill)
            {
                closeAt = Host.Now;
                host.Kill();
            }
            else
            {
                host.Send("go");
                host.WaitForExit(60000);
                closeAt = host.FileTime("CLOSE-START");
            }

            host.WaitForExit(60000);
            var hostExit = host.ExitFileTime();
            var exits = new List<(string Kind, int Id, double? Ms)>();
            foreach (var process in processes)
            {
                var exitTime = ProcessTools.WaitForExit(process.Handle, 30000);
                exits.Add((process.Kind, process.Id, exitTime is { } t ? (t - closeAt) / 10000.0 : null));
                ProcessTools.Close(process.Handle);
            }

            var leftovers = ProcessTools.Find(Paths.Marker(udf), keepHandles: false);
            var before = host.ResultBefore();
            var check = DataCheck(before, previousToken, previousStream);
            var allMs = exits.Select(e => e.Ms ?? double.PositiveInfinity).DefaultIfEmpty(0).Max();
            var browserMs = exits.Where(e => e.Kind == "browser").Select(e => e.Ms ?? double.PositiveInfinity).DefaultIfEmpty(double.NaN).First();
            var last = exits.OrderByDescending(e => e.Ms ?? double.PositiveInfinity).FirstOrDefault();
            var record = new StringBuilder();
            record.Append(CultureInfo.InvariantCulture, $"{{\"run\":{i},\"variant\":\"{variant}\",\"processes\":{processes.Count},");
            record.Append(CultureInfo.InvariantCulture, $"\"hostExitMs\":{(hostExit - closeAt) / 10000.0:F1},\"allExitMs\":{Json(allMs)},\"browserExitMs\":{Json(browserMs)},");
            record.Append(CultureInfo.InvariantCulture, $"\"last\":\"{last.Kind}\",\"leftovers\":{leftovers.Count},\"check\":\"{check}\",");
            record.Append(CultureInfo.InvariantCulture, $"\"exits\":[{string.Join(',', exits.Select(e => $"{{\"kind\":\"{e.Kind}\",\"ms\":{Json(e.Ms ?? double.PositiveInfinity)}}}"))}],");
            record.Append(CultureInfo.InvariantCulture, $"\"browserExitedEvent\":{(host.Has("BROWSER-EXITED") ? (host.FileTime("BROWSER-EXITED") - closeAt) / 10000.0 : -1):F1},");
            record.Append(CultureInfo.InvariantCulture, $"\"readyMs\":{host.ReadyMs:F0},\"cleanMs\":{Json(cleanMs)},\"launchToExitMs\":{Stopwatch.GetElapsedTime(launchStart).TotalMilliseconds:F0}}}");
            output.WriteLine(record.ToString());
            output.Flush();
            records.Add(JsonDocument.Parse(record.ToString()).RootElement.Clone());
            Console.WriteLine($"run {i,2}: processes={processes.Count} all-exit={allMs,7:F0} ms browser-exit={browserMs,7:F0} ms host-exit={(hostExit - closeAt) / 10000.0,6:F0} ms last={last.Kind} leftovers={leftovers.Count} data={check}");

            previousToken = token;
            previousStream = host.StreamCommitted();
            if (leftovers.Count > 0)
            {
                Console.WriteLine("  leftover processes: " + string.Join(',', leftovers.Select(l => $"{l.Id}:{l.Kind}")));
            }
        }

        Summarize(variant, records);
        return 0;
    }

    private static string DataCheck(JsonElement? before, string? previousToken, long previousStream)
    {
        if (previousToken is null)
        {
            return "first";
        }

        if (before is not { } state)
        {
            return "no-result";
        }

        var missing = new List<string>();
        if (Text(state, "ls") != previousToken) missing.Add("localStorage");
        if (Text(state, "idb") != previousToken) missing.Add("indexedDB");
        if (!(Text(state, "cookie") ?? "").Contains("token=" + previousToken, StringComparison.Ordinal)) missing.Add("cookie");
        if (Text(state, "cacheStorage") != previousToken) missing.Add("cacheStorage");
        if (previousStream >= 0)
        {
            var max = state.TryGetProperty("streamMax", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : -1;
            if (max < previousStream) missing.Add($"stream({max}<{previousStream})");
        }

        var unload = state.TryGetProperty("lsUnload", out var unloadValue) ? string.Join('|', unloadValue.EnumerateArray().Select(v => v.GetString())) : "";
        var unloadNote = unload.Contains(previousToken, StringComparison.Ordinal) ? "+unload:" + string.Join('/', unloadValue.EnumerateArray().Select(v => v.GetString()!.Split('=')[0].Replace("unload-", "", StringComparison.Ordinal)).Where(v => unload.Contains(v + "=" + previousToken, StringComparison.Ordinal))) : "";
        return (missing.Count == 0 ? "ok" : "LOST:" + string.Join('+', missing)) + unloadNote;
    }

    private static void Summarize(string variant, List<JsonElement> records)
    {
        double[] Values(string name) => records.Select(r => r.GetProperty(name)).Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).Order().ToArray();
        var all = Values("allExitMs");
        var browser = Values("browserExitMs");
        var host = Values("hostExitMs");
        Console.WriteLine($"SUMMARY variant={variant} runs={records.Count}");
        Console.WriteLine($"  all processes exited: {Stats(all)}; over 2 s: {all.Count(v => v > 2000)}; not exited in 30 s: {records.Count(r => r.GetProperty("allExitMs").ValueKind != JsonValueKind.Number)}");
        Console.WriteLine($"  browser process exited: {Stats(browser)}");
        Console.WriteLine($"  host exited after Close: {Stats(host)}");
        Console.WriteLine($"  launch to READY: {Stats(Values("readyMs"))}; launch to all processes gone: {Stats(Values("launchToExitMs"))}; folder clean before run: {Stats(Values("cleanMs"))}");
        Console.WriteLine($"  slowest process kinds: {string.Join(' ', records.GroupBy(r => r.GetProperty("last").GetString()).Select(g => $"{g.Key}={g.Count()}"))}");
        Console.WriteLine($"  data: {string.Join(' ', records.GroupBy(r => r.GetProperty("check").GetString()).Select(g => $"{g.Key}={g.Count()}"))}");
        Console.WriteLine($"  leftovers after exit wait: {records.Sum(r => r.GetProperty("leftovers").GetInt32())}");
        var buckets = new[] { 250, 500, 1000, 2000, 4000, 6000, 7000, 10000, 30000 };
        Console.WriteLine("  histogram (all exited, ms): " + string.Join(' ', buckets.Select((b, i) => $"<={b}:{all.Count(v => v <= b && (i == 0 || v > buckets[i - 1]))}")));
    }

    // ---- Q2: immediate relaunch on the same UDF -----------------------------------------------------------------

    private static int Relaunch(Options options)
    {
        var udf = options.Get("udf") ?? "relaunch";
        var runs = options.Int("runs", 30);
        var alternate = options.Get("alt-args");
        var hostArgs = Split(options.Get("host"));
        var variant = options.Get("variant") ?? (alternate is null ? "same" : "alt-args");
        using var output = File.CreateText(Path.Combine(Paths.Output, $"relaunch-{variant}-{Stamp()}.jsonl"));
        if (!Clean(udf))
        {
            return 1;
        }

        nint previousBrowser = 0;
        uint previousPid = 0;
        string? previousToken = null;
        int attached = 0, fresh = 0, failed = 0, dataOk = 0, prevAliveCount = 0;
        var envTimes = new List<double>();
        var readyTimes = new List<double>();
        var controllerTimes = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var token = $"r{i}";
            var args = new List<string> { "--scenario", "shutdown", "--udf", udf, "--handshake", "--page", $"op=readwrite&token={token}" };
            args.AddRange(hostArgs);
            if (alternate is not null && i % 2 == 1)
            {
                args.Add("--browser-args");
                args.Add(alternate);
            }

            var previousAlive = previousBrowser != 0 && ProcessTools.ExitTime(previousBrowser) is null;
            using var host = HostProcess.Start([.. args]);
            var ready = host.WaitFor("READY", 60000);
            var envLine = host.Last("ENV ") ?? host.Last("ENV-FAIL") ?? "";
            var pid = host.BrowserPid();
            var same = previousPid != 0 && pid == previousPid;
            var envMs = host.Number("ENV ", "ms");
            var controllerMs = host.Number("CONTROLLER ", "ms");
            if (ready)
            {
                host.Send("go");
                envTimes.Add(envMs);
                controllerTimes.Add(controllerMs);
                readyTimes.Add(host.ReadyMs);
            }

            host.WaitForExit(60000);
            var before = host.ResultBefore();
            var dataGood = previousToken is null || (before is { } b && Text(b, "ls") == previousToken && Text(b, "idb") == previousToken);
            if (!ready) failed++;
            else if (same) attached++;
            else fresh++;
            if (previousAlive) prevAliveCount++;
            if (ready && dataGood && previousToken is not null) dataOk++;
            var line = $"{{\"run\":{i},\"previousBrowserAlive\":{(previousAlive ? "true" : "false")},\"ready\":{(ready ? "true" : "false")},\"browserPid\":{pid},\"samePidAsPrevious\":{(same ? "true" : "false")}," +
                $"\"envMs\":{Json(envMs)},\"controllerMs\":{Json(controllerMs)},\"readyMs\":{host.ReadyMs:F0},\"data\":{(dataGood ? "true" : "false")},\"env\":\"{envLine.Replace("\"", "'", StringComparison.Ordinal)}\",\"failure\":\"{(host.Last("HOST-FAILURE") ?? "").Replace("\"", "'", StringComparison.Ordinal)[..Math.Min(200, (host.Last("HOST-FAILURE") ?? "").Length)]}\"}}";
            output.WriteLine(line);
            output.Flush();
            Console.WriteLine($"run {i,2}: prev-browser-alive={previousAlive} ready={ready} pid={pid} same-as-prev={same} env={envMs:F0} ms controller={controllerMs:F0} ms ready={host.ReadyMs:F0} ms data={dataGood} {(ready ? "" : envLine)}");

            ProcessTools.Close(previousBrowser);
            previousBrowser = pid != 0 ? ProcessTools.Open((int)pid) : 0;
            previousPid = pid;
            previousToken = ready ? token : previousToken;
        }

        ProcessTools.Close(previousBrowser);
        var gone = WaitGone(udf, 30000);
        Console.WriteLine($"SUMMARY relaunch variant={variant} runs={runs} previous-browser-alive-at-start={prevAliveCount} attached-to-previous-browser={attached} new-browser={fresh} failed={failed} data-ok={dataOk}/{runs - 1} all-gone-at-end={gone}");
        Console.WriteLine($"  environment creation: {Stats([.. envTimes.Order()])}");
        Console.WriteLine($"  controller creation: {Stats([.. controllerTimes.Order()])}");
        Console.WriteLine($"  launch to READY: {Stats([.. readyTimes.Order()])}");
        return 0;
    }

    // ---- Q2: a second instance while the first runs ---------------------------------------------------------------

    private static int Concurrent(Options options)
    {
        var udf = options.Get("udf") ?? "concurrent";
        var variant = options.Get("variant") ?? "same";
        var runs = options.Int("runs", 3);
        List<string> first = ["--scenario", "shutdown", "--udf", udf, "--handshake", "--page", "op=read"];
        List<string> second = ["--scenario", "shutdown", "--udf", udf, "--handshake", "--page", "op=read"];
        switch (variant)
        {
            case "same": break;
            case "args": second.AddRange(["--browser-args", "--disable-features=msSmartScreenProtection"]); break;
            case "language": second.AddRange(["--language", "de-DE"]); break;
            case "exclusiveB": second.AddRange(["--exclusive", "1"]); break;
            case "exclusiveA": first.AddRange(["--exclusive", "1"]); break;
            case "profile": second[second.IndexOf("--udf") + 1] = udf; second.AddRange(["--windows", udf + "/second"]); break;
            default: throw new ArgumentException("variant");
        }

        if (!Clean(udf))
        {
            return 1;
        }

        for (var i = 0; i < runs; i++)
        {
            using var a = HostProcess.Start([.. first]);
            if (!a.WaitFor("READY", 60000))
            {
                Console.WriteLine($"run {i}: A failed: {a.Last("HOST-FAILURE")}");
                continue;
            }

            using var b = HostProcess.Start([.. second]);
            var bReady = b.WaitFor("READY", 60000);
            var bEnv = b.Last("ENV-FAIL") ?? b.Last("ENV ") ?? "";
            var bController = b.Last("CONTROLLER-FAIL") ?? b.Last("CONTROLLER ") ?? "";
            if (bReady)
            {
                b.Send("go");
            }

            b.WaitForExit(30000);
            var aAlive = !a.HasExited;
            a.Send("go");
            a.WaitForExit(30000);
            var gone = WaitGone(udf, 30000);
            Console.WriteLine($"run {i}: variant={variant} A-pid={a.BrowserPid()} B-ready={bReady} B-pid={b.BrowserPid()} shared={(bReady && a.BrowserPid() == b.BrowserPid())} " +
                $"B-env-ms={b.Number("ENV ", "ms"):F0} B-exit={b.ExitCodeText} A-alive-after-B={aAlive} gone={gone}");
            Console.WriteLine($"  B: {Trim(bEnv)}");
            Console.WriteLine($"  B: {Trim(bController)}");
            if (b.Last("HOST-FAILURE") is { } failure)
            {
                Console.WriteLine($"  B failure: {Trim(failure)}");
            }

            using var log = File.CreateText(Path.Combine(Paths.Output, $"concurrent-{variant}-{i}-{Stamp()}.log"));
            log.WriteLine("== A"); log.WriteLine(string.Join('\n', a.Lines));
            log.WriteLine("== B"); log.WriteLine(string.Join('\n', b.Lines));
        }

        return 0;
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    /// <summary>Deletes a spike UDF, but only after confirming no browser process of it is running.</summary>
    public static bool Clean(string udf)
    {
        if (!WaitGone(udf, 30000))
        {
            Console.WriteLine($"UDF {udf} still has running processes; not deleting.");
            return false;
        }

        return Paths.TryDelete(Paths.Udf(udf));
    }

    private static bool WaitGone(string udf, int timeoutMilliseconds)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)timeoutMilliseconds * Stopwatch.Frequency / 1000;
        while (ProcessTools.Find(Paths.Marker(udf), keepHandles: false).Count > 0)
        {
            if (Stopwatch.GetTimestamp() > deadline)
            {
                return false;
            }

            Thread.Sleep(50);
        }

        return true;
    }

    private static string Trim(string line) => line.Length > 300 ? line[..300] : line;

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

    /// <summary>A host child process with its stdout captured line by line.</summary>
    private sealed class HostProcess : IDisposable
    {
        private readonly Process _process;
        private readonly nint _handle;
        private readonly List<string> _lines = [];
        private readonly long _startedAt = Host.Now;

        private HostProcess(Process process)
        {
            _process = process;
            _handle = ProcessTools.Open(process.Id);
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
            process.BeginOutputReadLine();
        }

        public List<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public bool HasExited => _process.HasExited;

        public string ExitCodeText => _process.HasExited ? _process.ExitCode.ToString(CultureInfo.InvariantCulture) : "running";

        public double ReadyMs => Has("READY") ? (FileTime("READY") - _startedAt) / 10000.0 : double.NaN;

        public static HostProcess Start(string[] args)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
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
            lock (_lines)
            {
                while (true)
                {
                    if (_lines.Any(l => l.StartsWith(key + " ", StringComparison.Ordinal)))
                    {
                        return true;
                    }

                    if (_lines.Any(l => l.StartsWith("HOST-EXIT ", StringComparison.Ordinal)) || Stopwatch.GetTimestamp() > deadline)
                    {
                        return false;
                    }

                    Monitor.Wait(_lines, 50);
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

        public long ExitFileTime() => ProcessTools.ExitTime(_handle) ?? Host.Now;

        public bool Has(string key) => Last(key) is not null;

        public string? Last(string key)
        {
            lock (_lines)
            {
                return _lines.LastOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
            }
        }

        public long FileTime(string key)
        {
            var line = Last(key + " ") ?? throw new InvalidOperationException("No " + key);
            return long.Parse(Field(line, "ft")!, CultureInfo.InvariantCulture);
        }

        public double Number(string key, string field) =>
            Last(key) is { } line && Field(line, field) is { } value ? double.Parse(value, CultureInfo.InvariantCulture) : double.NaN;

        public uint BrowserPid() =>
            Last("CONTROLLER ") is { } line && Field(line, "browserPid") is { } value ? uint.Parse(value, CultureInfo.InvariantCulture) : 0;

        public JsonElement? ResultBefore()
        {
            if (Last("RESULT ") is not { } line)
            {
                return null;
            }

            var json = line[(line.IndexOf("json=", StringComparison.Ordinal) + 5)..];
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("before", out var before) ? before.Clone() : null;
        }

        public long StreamCommitted()
        {
            if (Last("RESULT ") is not { } line)
            {
                return -1;
            }

            using var document = JsonDocument.Parse(line[(line.IndexOf("json=", StringComparison.Ordinal) + 5)..]);
            return document.RootElement.TryGetProperty("streamCommitted", out var value) ? value.GetInt64() : -1;
        }

        public void Dispose()
        {
            ProcessTools.Close(_handle);
            _process.Dispose();
        }

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
