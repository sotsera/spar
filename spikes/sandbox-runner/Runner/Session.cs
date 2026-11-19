using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SandboxRunner;

/// <summary>One payload to run in the guest: a published test executable (or any program) and how to run it.</summary>
internal sealed record Job(string PayloadFolder, string Executable, string RunAs, TimeSpan Deadline, string Arguments, bool XunitReports, bool Local)
{
    /// <summary>
    /// <c>folder|exe|user or system|deadline seconds|arguments|xunit or plain|mapped or local</c>. "local" copies the
    /// payload to the guest's own disk under a folder that holds an empty <c>Spar.slnx</c>, for tests that look for the
    /// repository root (Spar's own budget tests write there).
    /// </summary>
    public static Job Parse(string text)
    {
        var parts = text.Split('|');
        return new Job(
            Path.GetFullPath(parts[0]),
            parts[1],
            parts.Length > 2 && parts[2] == "system" ? "System" : "ExistingLogin",
            TimeSpan.FromSeconds(parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 600),
            parts.Length > 4 ? parts[4] : "",
            parts.Length <= 5 || parts[5] != "plain",
            parts.Length > 6 && parts[6] == "local");
    }
}

internal sealed record SessionOptions(string Name, string Root, IReadOnlyList<Job> Jobs, bool Prestage, string Connect, bool AllowRunning);

/// <summary>
/// The runner's whole cycle for one sandbox: configuration, start, an optional minimized session, staging under new
/// names, runs with deadlines, results through the mapped output folder, stop, and the client windows closed.
/// </summary>
internal sealed partial class Session(SessionOptions options)
{
    private const string GuestIn = @"C:\spar\in";
    private const string GuestOut = @"C:\spar\out";
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WsbTimeout = TimeSpan.FromSeconds(120);

    private readonly string _base = Path.Combine(options.Root, "sessions", options.Name);
    private readonly HashSet<nint> _windowsBefore = [];
    private readonly HashSet<nint> _ourWindows = [];
    private readonly Lock _windowsGate = new();
    private Timeline _timeline = null!;
    private Process? _connect;
    private string _id = "";

    private string HostIn => Path.Combine(_base, "in");

    private string HostOut => Path.Combine(_base, "out");

    public async Task<int> RunAsync()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }

        Directory.CreateDirectory(HostIn);
        Directory.CreateDirectory(HostOut);
        using var timeline = _timeline = new Timeline(Path.Combine(_base, "timeline.jsonl"));
        var failures = 0;

        var (running, list) = await Wsb.ListAsync();
        _timeline.Write("list-before", list.Elapsed, ("ids", string.Join(",", running)), ("raw", list.OneLine), ("serverProcesses", Process.GetProcessesByName("WindowsSandboxServer").Length), ("clientProcesses", Process.GetProcessesByName("WindowsSandboxRemoteSession").Length));
        if (running.Count > 0 && !options.AllowRunning)
        {
            // Only one sandbox runs per user session: a second start fails, so report the one already running.
            _timeline.Write("refused", null, ("reason", "a sandbox is already running; only one runs per user session"));
            return 2;
        }

        var staged = new Dictionary<Job, string>();
        if (options.Prestage)
        {
            foreach (var job in options.Jobs)
            {
                staged[job] = Stage(job, staged.Count);
            }
        }

        var config = Configuration();
        await File.WriteAllTextAsync(Path.Combine(_base, "config.xml"), config);
        _id = Guid.NewGuid().ToString();
        var sessionClock = Stopwatch.StartNew();
        var start = await Wsb.RunAsync(TimeSpan.FromMinutes(5), "start", "--id", _id, "--config", config, "--raw");
        _timeline.Write("start", start.Elapsed, ("id", "<id>"), ("raw", start.OneLine.Replace(_id, "<id>", StringComparison.Ordinal)), ("clientWindows", ClientWindows.List().Count));
        if (start.ProcessExitCode != 0)
        {
            return 3;
        }

        try
        {
            var ready = await WaitForExecAsync("System", sessionClock);
            if (!ready)
            {
                return 4;
            }

            var needsUser = options.Jobs.Any(j => j.RunAs == "ExistingLogin");
            if (options.Connect == "always" || (options.Connect == "auto" && needsUser))
            {
                if (!await ConnectAsync(sessionClock))
                {
                    return 5;
                }
            }

            for (var index = 0; index < options.Jobs.Count; index++)
            {
                var job = options.Jobs[index];
                var runId = staged.TryGetValue(job, out var prestaged) ? prestaged : Stage(job, index);
                failures += await RunJobAsync(job, runId, staged.ContainsKey(job)) ? 0 : 1;
            }
        }
        finally
        {
            await StopAsync(sessionClock);
        }

        return failures == 0 ? 0 : 1;
    }

    private string Configuration()
    {
        // Everything a test does not ask for is off: network, clipboard, printers, camera and microphone. The payload
        // folder is read-only in the guest; only the output folder is writable.
        return $"""
            <Configuration>
              <Networking>Disable</Networking>
              <ClipboardRedirection>Disable</ClipboardRedirection>
              <PrinterRedirection>Disable</PrinterRedirection>
              <VideoInput>Disable</VideoInput>
              <AudioInput>Disable</AudioInput>
              <MappedFolders>
                <MappedFolder><HostFolder>{HostIn}</HostFolder><SandboxFolder>{GuestIn}</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
                <MappedFolder><HostFolder>{HostOut}</HostFolder><SandboxFolder>{GuestOut}</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
              </MappedFolders>
            </Configuration>
            """;
    }

    /// <summary>Copies the payload under a new folder name (the guest caches replaced files), the marker file last.</summary>
    private string Stage(Job job, int index)
    {
        var runId = $"j{index + 1:D2}-{Guid.NewGuid().ToString("N")[..6]}";
        var clock = Stopwatch.StartNew();
        var target = Path.Combine(HostIn, runId);
        Directory.CreateDirectory(target);
        long bytes = 0;
        var files = 0;
        foreach (var file in Directory.EnumerateFiles(job.PayloadFolder))
        {
            if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            bytes += new FileInfo(file).Length;
            files++;
        }

        File.WriteAllText(Path.Combine(target, "run.cmd"), GuestScript(job, runId).ReplaceLineEndings("\r\n"));
        File.WriteAllText(Path.Combine(target, "ready.txt"), runId);
        _timeline.Write("stage-copy", clock.Elapsed, ("run", runId), ("files", files), ("bytes", bytes), ("exe", job.Executable));
        return runId;
    }

    private static string GuestScript(Job job, string runId)
    {
        var reports = job.XunitReports ? $"-noColor -result-trx \"%OUT%\\results.trx\" -result-xml \"%OUT%\\results.xml\" " : "";
        var work = $@"C:\spar-work\{runId}";
        var folder = job.Local ? $@"{work}\repo\bin" : $@"{GuestIn}\{runId}";
        var local = job.Local
            ? $"""
              >> "%OUT%\guest-env.txt" echo local-copy-start=%TIME%
              robocopy "{GuestIn}\{runId}" "{folder}" /e /njh /njs /nfl /ndl /np > nul
              type nul > "{work}\repo\Spar.slnx"
              >> "%OUT%\guest-env.txt" echo local-copy-end=%TIME%
              """
            : "";
        var collectLocal = job.Local ? $"if exist \"{work}\\repo\\artifacts\\test-results\" robocopy \"{work}\\repo\\artifacts\\test-results\" \"%OUT%\\test-results\" /e /njh /njs /nfl /ndl /np > nul" : "";

        // Redirections come first: "echo 1> file" would redirect stream 1 and write "ECHO is off.", and parentheses
        // around an echo break on the ")" of "Program Files (x86)".
        return $"""
            @echo off
            setlocal
            set OUT={GuestOut}\{runId}
            mkdir "%OUT%" 2>nul
            set SPIKE_ARTIFACTS={work}\artifacts
            mkdir "%SPIKE_ARTIFACTS%" 2>nul
            for /d %%d in ("C:\Program Files (x86)\Microsoft\EdgeWebView\Application\1*") do set SPIKE_WEBVIEW2_BROWSER_FOLDER=%%d
            > "%OUT%\guest-env.txt" echo begin=%TIME%
            >> "%OUT%\guest-env.txt" echo browser=%SPIKE_WEBVIEW2_BROWSER_FOLDER%
            >> "%OUT%\guest-env.txt" whoami
            >> "%OUT%\guest-env.txt" 2>&1 where dotnet
            {local}
            >> "%OUT%\guest-env.txt" echo exe-start=%TIME%
            "{folder}\{job.Executable}" {reports}{job.Arguments} > "%OUT%\console.log" 2>&1
            set RC=%ERRORLEVEL%
            >> "%OUT%\guest-env.txt" echo exe-end=%TIME%
            {collectLocal}
            > "%OUT%\exitcode.tmp" echo %RC%
            move /y "%OUT%\exitcode.tmp" "%OUT%\exitcode.txt" >nul
            exit /b %RC%
            """;
    }

    private async Task<bool> WaitForExecAsync(string runAs, Stopwatch sessionClock)
    {
        var clock = Stopwatch.StartNew();
        var attempts = 0;
        WsbResult? last = null;
        while (clock.Elapsed < TimeSpan.FromMinutes(3))
        {
            attempts++;
            last = await Wsb.ExecAsync(_id, runAs, "cmd /c exit 0", WsbTimeout);
            if (last.GuestExitCode == 0)
            {
                _timeline.Write($"exec-ready-{runAs}", clock.Elapsed, ("attempts", attempts), ("sinceStart_ms", sessionClock.Elapsed.TotalMilliseconds), ("lastCall_ms", last.Elapsed.TotalMilliseconds));
                return true;
            }

            await Task.Delay(Poll);
        }

        _timeline.Write($"exec-not-ready-{runAs}", clock.Elapsed, ("attempts", attempts), ("last", last?.OneLine));
        return false;
    }

    private async Task<bool> ConnectAsync(Stopwatch sessionClock)
    {
        foreach (var window in ClientWindows.List())
        {
            _windowsBefore.Add(window.Handle);
        }

        var clock = Stopwatch.StartNew();
        _connect = Wsb.StartConnect(_id);
        _ = Task.Run(() => KeepMinimizedAsync(clock));
        var ready = await WaitForExecAsync("ExistingLogin", sessionClock);
        _timeline.Write("connect", clock.Elapsed, ("sessionReady", ready), ("connectExited", _connect.HasExited));
        return ready;
    }

    /// <summary>
    /// The client restores and activates itself when the connection completes, after an early minimize, so the runner
    /// minimizes every new client window whenever it is not minimized, until the sandbox stops.
    /// </summary>
    private async Task KeepMinimizedAsync(Stopwatch clock)
    {
        var minimizes = 0;
        while (_connect is not null && !_stopping)
        {
            foreach (var window in ClientWindows.List())
            {
                if (_windowsBefore.Contains(window.Handle))
                {
                    continue;
                }

                bool added;
                lock (_windowsGate)
                {
                    added = _ourWindows.Add(window.Handle);
                }

                if (added)
                {
                    _timeline.Write("client-window", null, ("since_connect_ms", clock.Elapsed.TotalMilliseconds), ("visible", window.Visible), ("minimized", window.Minimized), ("foreground", window.Foreground), ("title", window.Title));
                }

                if (window.Visible && !window.Minimized)
                {
                    ClientWindows.Minimize(window.Handle);
                    minimizes++;
                    _timeline.Write("client-minimize", null, ("since_connect_ms", clock.Elapsed.TotalMilliseconds), ("n", minimizes), ("wasForeground", window.Foreground));
                }
            }

            await Task.Delay(100);
        }
    }

    private volatile bool _stopping;

    private async Task<bool> RunJobAsync(Job job, string runId, bool prestaged)
    {
        // Staged files appear in the guest after a delay; poll for the marker written last.
        var visible = Stopwatch.StartNew();
        var attempts = 0;
        var seen = false;
        while (visible.Elapsed < TimeSpan.FromMinutes(2))
        {
            attempts++;
            var probe = await Wsb.ExecAsync(_id, "System", $@"cmd /c if exist {GuestIn}\{runId}\ready.txt (exit 0) else (exit 1)", WsbTimeout);
            if (probe.GuestExitCode == 0)
            {
                seen = true;
                break;
            }

            await Task.Delay(Poll);
        }

        _timeline.Write("stage-visible", visible.Elapsed, ("run", runId), ("seen", seen), ("attempts", attempts), ("prestaged", prestaged));
        if (!seen)
        {
            return false;
        }

        var run = await Wsb.ExecAsync(_id, job.RunAs, $@"cmd /c {GuestIn}\{runId}\run.cmd", job.Deadline);
        _timeline.Write("run", run.Elapsed, ("run", runId), ("as", job.RunAs), ("guestExit", run.GuestExitCode), ("timedOut", run.TimedOut), ("raw", run.OneLine));
        if (run.TimedOut)
        {
            // The deadline ended the wait, not the guest process: end it explicitly before collecting.
            var kill = await Wsb.ExecAsync(_id, "System", $"cmd /c taskkill /f /t /im {job.Executable}", WsbTimeout);
            _timeline.Write("deadline-kill", kill.Elapsed, ("run", runId), ("guestExit", kill.GuestExitCode));
        }

        // Results come back as files in the mapped output folder; exitcode.txt is moved into place last.
        var collect = Stopwatch.StartNew();
        var exitFile = Path.Combine(HostOut, runId, "exitcode.txt");
        while (!File.Exists(exitFile) && collect.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50);
        }

        var exitText = File.Exists(exitFile) ? (await File.ReadAllTextAsync(exitFile)).Trim() : null;
        var trx = Path.Combine(HostOut, runId, "results.trx");
        var counters = File.Exists(trx) ? Counters(await File.ReadAllTextAsync(trx)) : "no trx";
        var console = Path.Combine(HostOut, runId, "console.log");
        var summary = File.Exists(console) ? SummaryLine(await File.ReadAllTextAsync(console)) : "no console.log";
        _timeline.Write("collect", collect.Elapsed, ("run", runId), ("exitFile", exitText), ("trx", counters), ("summary", summary));
        return !run.TimedOut && run.GuestExitCode == 0 && exitText == "0";
    }

    private async Task StopAsync(Stopwatch sessionClock)
    {
        _stopping = true;
        var stop = await Wsb.RunAsync(TimeSpan.FromMinutes(2), "stop", "--id", _id, "--raw");
        _timeline.Write("stop", stop.Elapsed, ("raw", stop.OneLine.Replace(_id, "<id>", StringComparison.Ordinal)));

        var connectAlive = _connect is { HasExited: false };
        if (connectAlive)
        {
            _connect!.Kill(entireProcessTree: true);
        }

        // Each wsb connect leaves a client window behind after the stop: close the ones this session opened.
        var close = Stopwatch.StartNew();
        List<nint> ours;
        lock (_windowsGate)
        {
            ours = [.. _ourWindows];
        }

        foreach (var window in ClientWindows.List())
        {
            if (!_windowsBefore.Contains(window.Handle) && !ours.Contains(window.Handle))
            {
                ours.Add(window.Handle);
            }
        }

        var leftAfterStop = ours.Count(ClientWindows.Exists);
        foreach (var handle in ours.Where(ClientWindows.Exists))
        {
            ClientWindows.Close(handle);
        }

        while (ours.Any(ClientWindows.Exists) && close.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(50);
        }

        _timeline.Write("close-clients", close.Elapsed, ("connectStillRunning", connectAlive), ("windowsLeftAfterStop", leftAfterStop), ("windowsLeftAfterClose", ours.Count(ClientWindows.Exists)));
        var (running, list) = await Wsb.ListAsync();
        _timeline.Write("list-after", list.Elapsed, ("ids", string.Join(",", running).Replace(_id, "<id>", StringComparison.Ordinal)), ("oursStillListed", running.Contains(_id)), ("clientProcesses", Process.GetProcessesByName("WindowsSandboxRemoteSession").Length));
        _timeline.Write("total", sessionClock.Elapsed);
    }

    private static string Counters(string trx)
    {
        var match = CountersPattern().Match(trx);
        return match.Success ? match.Value : "no counters";
    }

    private static string SummaryLine(string console)
    {
        var line = console.Split('\n').LastOrDefault(l => l.Contains("Total:", StringComparison.Ordinal));
        return line?.Trim() ?? "no summary";
    }

    [GeneratedRegex("<Counters [^>]*>")]
    private static partial Regex CountersPattern();
}
