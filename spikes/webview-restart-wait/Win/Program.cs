// The Windows probe of spikes/webview-restart-wait. One process, one STA with a message pump, and cycles 0..reps that
// each create a controller (offscreen window, never activated), navigate to a page served from memory on
// https://app.localhost, optionally dwell, and close it. Cycle 0 is the first controller of the process; cycles 1..reps
// are the "next controller after the last one closed" the spike measures. Per cycle it writes one line: the time to
// the controller and to the first NavigationCompleted, the browser pid, whether it is a new browser, and (once known)
// when the previous browser exited after its close, by process handle and by BrowserProcessExited.
//   RestartWait.exe --root <folder> --out <log> [--reps 10] [--gap 1000] [--profile default|private|named]
//     [--smartscreen on|off] [--dwell 0] [--window shown|hidden] [--env keep|new] [--wait none|event|process]
//     [--folder same|per-cycle] [--overlap false] [--args "<browser arguments>"] [--exit wait|now] [--sample false]
using System.Collections.Concurrent;
using System.Runtime.InteropServices.Marshalling;
using RestartWait;
using RestartWait.Interop;

var options = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["--root"] = "", ["--out"] = "", ["--reps"] = "10", ["--gap"] = "1000", ["--profile"] = "default", ["--smartscreen"] = "off",
    ["--dwell"] = "0", ["--window"] = "shown", ["--env"] = "keep", ["--wait"] = "none", ["--folder"] = "same",
    ["--overlap"] = "false", ["--args"] = "", ["--exit"] = "wait", ["--sample"] = "false",
};
for (var i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i]] = args[i + 1];
}

if (options["--root"].Length == 0 || !options["--root"].Replace('/', '\\').Contains(@"\artifacts\webview2-profiles\webview-restart-wait\", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("--root must be under artifacts\\webview2-profiles\\webview-restart-wait\\");
    return 2;
}

if (!OperatingSystem.IsWindows())
{
    return 2;
}

var thread = new Thread(() => Environment.ExitCode = new Probe(options).Run());
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return Environment.ExitCode;

internal sealed class Probe(Dictionary<string, string> options)
{
    private readonly ConcurrentDictionary<uint, double> _exitAt = new();
    private readonly ConcurrentDictionary<uint, double> _exitEventAt = new();
    private readonly List<Cycle> _cycles = [];
    private readonly List<object> _keepAlive = [];
    private readonly HashSet<uint> _watched = [];
    private StreamWriter _log = StreamWriter.Null;
    private ICoreWebView2Environment10? _environment;
    private string _environmentFolder = "";
    private int _environments;

    private string Root => Path.GetFullPath(options["--root"]);

    public int Run()
    {
        var output = options["--out"].Length > 0 ? options["--out"] : Path.Combine(Root, "probe.log");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        _log = new StreamWriter(output, append: false) { AutoFlush = true };
        Note("options " + string.Join(" ", options.Where(o => o.Key is not "--root" and not "--out").Select(o => $"{o.Key} {Quote(o.Value)}")));
        try
        {
            Native.RegisterWindowClass();
            Execute();
            return 0;
        }
        catch (Exception exception)
        {
            Note("FAILED " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
        finally
        {
            _log.Dispose();
        }
    }

    private void Execute()
    {
        var reps = int.Parse(options["--reps"]);
        var gap = int.Parse(options["--gap"]);
        var dwell = int.Parse(options["--dwell"]);
        var overlap = options["--overlap"] == "true";
        View? open = null;
        for (var index = 0; index <= reps; index++)
        {
            var previous = _cycles.Count > 0 ? _cycles[^1] : null;
            var cycle = new Cycle { Index = index };
            if (previous is not null && !overlap)
            {
                var waitStarted = Native.Now;
                switch (options["--wait"])
                {
                    case "event":
                        Native.Pump(() => _exitEventAt.ContainsKey(previous.Pid), 30_000);
                        break;
                    case "process":
                        Native.Pump(() => _exitAt.ContainsKey(previous.Pid), 30_000);
                        break;
                }

                cycle.WaitedMs = Native.Now - waitStarted;
                Native.Pump(() => false, gap);
            }

            var folder = options["--folder"] == "per-cycle" ? Path.Combine(Root, $"udf-{index}") : Path.Combine(Root, "udf");
            if (_environment is null || options["--env"] == "new" || folder != _environmentFolder)
            {
                var started = Native.Now;
                CreateEnvironment(folder);
                cycle.EnvironmentMs = Native.Now - started;
            }

            cycle.Start = Native.Now;
            if (previous is not null)
            {
                cycle.PreviousExitedBeforeStart = _exitAt.TryGetValue(previous.Pid, out var exited) && exited < cycle.Start;
            }

            var view = new View(_environment!, options);
            view.Open(cycle);
            cycle.Pid = view.BrowserProcessId;
            cycle.NewBrowser = previous is null || previous.Pid != cycle.Pid;
            Watch(cycle.Pid);
            if (overlap && open is not null)
            {
                open.Close();
                previous!.CloseAt = Native.Now;
            }

            Native.Pump(() => false, dwell);
            if (!overlap || index == reps)
            {
                view.Close();
                cycle.CloseAt = Native.Now;
                open = null;
            }
            else
            {
                open = view;
            }

            _cycles.Add(cycle);
            Note(cycle.Describe(previous, _exitAt, _exitEventAt));
        }

        if (options["--exit"] == "now")
        {
            // As an app that exits once its last window is closed: no wait for the browser.
            Note("exit now, without waiting for the browser");
            Release();
            return;
        }

        var last = _cycles[^1];
        Native.Pump(() => _exitAt.ContainsKey(last.Pid) && _exitEventAt.ContainsKey(last.Pid), 30_000);
        foreach (var cycle in _cycles)
        {
            Note($"cycle {cycle.Index} browser {cycle.Pid}: close->exit +{Since(_exitAt, cycle.Pid, cycle.CloseAt)} ms, BrowserProcessExited +{Since(_exitEventAt, cycle.Pid, cycle.CloseAt)} ms");
        }
        Release();
        Native.Pump(() => false, 500);
        var left = Native.BrowserProcesses(Root);
        Note($"processes of the root left: {left.Count} {string.Join(", ", left.Select(p => $"{p.Kind}#{p.Id}"))}");
        Note(Summary());
    }

    private void CreateEnvironment(string folder)
    {
        if (_environment is not null)
        {
            ((ComObject)(object)_environment).FinalRelease();
            _environment = null;
        }

        Directory.CreateDirectory(folder);
        var environmentOptions = new EnvironmentOptions { AdditionalBrowserArguments = ExpandArguments(options["--args"]) };
        var handler = new EnvironmentCompletedHandler();
        var hr = NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, folder, environmentOptions, handler);
        if (hr < 0)
        {
            throw new ComFailureException("CreateCoreWebView2EnvironmentWithOptions", hr);
        }

        _environment = Native.Wait(handler.Task, 30_000);
        _environmentFolder = folder;
        _environments++;
        var exited = new BrowserProcessExitedHandler((pid, kind) =>
        {
            _exitEventAt.TryAdd(pid, Native.Now);
            Note($"  BrowserProcessExited pid {pid} kind {kind}");
        });
        _environment.AddBrowserProcessExited(exited);
        _keepAlive.Add(exited);
        if (_environments == 1)
        {
            Note("Runtime " + _environment.GetBrowserVersionString());
        }
    }

    /// <summary>{n} in the arguments becomes the environment's number, so each browser can log to its own file.</summary>
    private string ExpandArguments(string value) => value.Replace("{n}", (_environments + 1).ToString(), StringComparison.Ordinal);

    private void Watch(uint pid)
    {
        if (_watched.Add(pid) && !Native.WatchExit(pid, at => _exitAt.TryAdd(pid, at)))
        {
            Note($"  could not open browser {pid}");
        }
    }

    private void Release()
    {
        if (_environment is not null)
        {
            ((ComObject)(object)_environment).FinalRelease();
            _environment = null;
        }
    }

    private string Summary()
    {
        var restarts = _cycles.Skip(1).ToList();
        if (restarts.Count == 0)
        {
            return "summary: no restarts";
        }

        var created = restarts.Select(c => c.ControllerMs).Order().ToList();
        var navigated = restarts.Select(c => c.ControllerMs + c.NavigationMs).Order().ToList();
        var exits = _cycles.Where(c => _exitAt.ContainsKey(c.Pid) && c.CloseAt > 0 && _exitAt[c.Pid] > c.CloseAt)
            .Select(c => _exitAt[c.Pid] - c.CloseAt).Order().ToList();
        return $"summary: first controller {_cycles[0].ControllerMs:0} ms; restarts n={restarts.Count} controller p50 {Median(created):0} ms " +
            $"(min {created[0]:0}, max {created[^1]:0}), to first navigation p50 {Median(navigated):0} ms, multi-second (>=2000 ms) {created.Count(v => v >= 2000)}, " +
            $"new browser {restarts.Count(c => c.NewBrowser)}; close->exit p50 {(exits.Count > 0 ? Median(exits).ToString("0") : "-")} ms " +
            $"(max {(exits.Count > 0 ? exits[^1].ToString("0") : "-")})";
    }

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;

    internal static string Since(ConcurrentDictionary<uint, double> times, uint pid, double from) =>
        times.TryGetValue(pid, out var at) ? (at - from).ToString("0") : "?";

    private static string Quote(string value) => value.Length == 0 || value.Contains(' ') ? $"\"{value}\"" : value;

    private void Note(string text) => _log.WriteLine($"{Native.Now,10:0.0} | {text}");
}

internal sealed class Cycle
{
    public int Index { get; init; }

    public double Start { get; set; }

    public double EnvironmentMs { get; set; } = -1;

    public double WaitedMs { get; set; }

    public double ControllerMs { get; set; }

    public double NavigationMs { get; set; }

    public double CloseAt { get; set; }

    public uint Pid { get; set; }

    public bool NewBrowser { get; set; }

    public bool? PreviousExitedBeforeStart { get; set; }

    public List<string> Samples { get; } = [];

    public string Describe(Cycle? previous, ConcurrentDictionary<uint, double> exitAt, ConcurrentDictionary<uint, double> exitEventAt)
    {
        var text = $"cycle {Index}: controller {ControllerMs:0} ms, navigation {NavigationMs:0} ms, browser {Pid} new {NewBrowser}";
        if (EnvironmentMs >= 0)
        {
            text += $", environment {EnvironmentMs:0} ms";
        }

        if (previous is not null)
        {
            var gap = Start - previous.CloseAt;
            text += $", close->create {gap:0} ms (waited {WaitedMs:0}), previous browser exited before create {PreviousExitedBeforeStart}" +
                $", previous exit +{Probe.Since(exitAt, previous.Pid, previous.CloseAt)} ms / event +{Probe.Since(exitEventAt, previous.Pid, previous.CloseAt)} ms after its close";
        }

        if (Samples.Count > 0)
        {
            text += "; while creating: " + string.Join("; ", Samples);
        }

        return text;
    }
}
