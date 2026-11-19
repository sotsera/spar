using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>
/// One running sandbox of a run: started with its configuration, ready once a command runs as SYSTEM and the runner's
/// marker is written, connected to its user session only when a job needs it, running jobs one after another, and
/// stopped with every client window its connect opened closed. Its folders are the run's: <c>in</c> (staging, mapped
/// read-only) and <c>out</c> (mapped writable), each job under a new folder name there, since the guest keeps reading
/// cached copies of a mapped file that was replaced.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class SandboxSession : IAsyncDisposable
{
    private static readonly TimeSpan CallLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    private readonly string _id = Guid.NewGuid().ToString();
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly string _input;
    private readonly string _output;
    private readonly SandboxTimeline _timeline;
    private readonly HashSet<nint> _windowsBefore = [];
    private readonly HashSet<nint> _windowsOurs = [];
    private readonly Lock _windowsGate = new();
    private readonly CancellationTokenSource _stopping = new();
    private Process? _connect;
    private Task? _minimizer;
    private int _jobs;
    private bool _started;

    private SandboxSession(string input, string output, SandboxTimeline timeline)
    {
        _input = input;
        _output = output;
        _timeline = timeline;
        _timeline.Hide(_id);
    }

    /// <summary>The session's timeline, for the run's summary.</summary>
    public SandboxTimeline Timeline => _timeline;

    /// <summary>How many times the Sandbox client window was minimized: each was a time it had taken the host's foreground or could have.</summary>
    public int Minimizes { get; private set; }

    /// <summary>Starts a sandbox and waits until it runs a command as SYSTEM; the caller disposes it, which stops it.</summary>
    public static async Task<SandboxSession> StartAsync(
        string folder,
        SandboxFeatures features,
        TimeSpan readyLimit,
        CancellationToken cancellationToken)
    {
        var input = Path.Combine(folder, "in");
        var output = Path.Combine(folder, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        var session = new SandboxSession(input, output, new SandboxTimeline(Path.Combine(output, "timeline.jsonl")));
        try
        {
            await session.StartAsync(features, readyLimit, cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>Runs one job: staged, its files seen by the guest, run in its lane with its deadline, its results collected.</summary>
    public async Task<SandboxJobResult> RunAsync(SandboxJob job, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        if (job.Lane == SandboxLane.UserSession)
        {
            await ConnectAsync(cancellationToken);
        }

        var stage = Stage(job);
        var guestStage = $@"{GuestScript.GuestInput}\{stage}";
        await WaitAsync(
            "stage-visible",
            SandboxLane.System,
            $@"cmd /c if exist {guestStage}\ready.txt (exit 0) else (exit 1)",
            CallLimit,
            cancellationToken);

        var run = await Wsb.ExecAsync(_id, job.Lane, $@"cmd /c {guestStage}\run.cmd", job.Deadline, cancellationToken);
        _timeline.Write(
            "run",
            run.Elapsed,
            ("job", job.Name),
            ("stage", stage),
            ("lane", job.Lane.ToString()),
            ("guestExit", run.GuestExitCode),
            ("timedOut", run.TimedOut),
            ("wsb", run.TimedOut ? "" : run.OneLine));
        if (run.TimedOut)
        {
            // The deadline ended the wait for wsb, not the guest's process: end it there before collecting.
            var kill = await Wsb.ExecAsync(_id, SandboxLane.System, $"taskkill /f /t /im {job.Executable}", CallLimit, cancellationToken);
            _timeline.Write("deadline-kill", kill.Elapsed, ("job", job.Name), ("guestExit", kill.GuestExitCode));
        }

        var outputFolder = Path.Combine(_output, stage);
        var exitCode = await ExitCodeAsync(outputFolder, cancellationToken);
        if (job.CollectAfter > TimeSpan.Zero)
        {
            await Task.Delay(job.CollectAfter, cancellationToken);
        }

        await CollectLogsAsync(stage, cancellationToken);
        if (job.CollectAfter > TimeSpan.Zero)
        {
            // A job that ends the guest's session: whether the sandbox is still listed and a user session answers again.
            var listed = await Wsb.ListAsync(cancellationToken);
            var user = await Wsb.ExecAsync(_id, SandboxLane.UserSession, "cmd /c exit 0", CallLimit, cancellationToken);
            _timeline.Write(
                "after-session-end",
                user.Elapsed,
                ("listed", listed?.Contains(_id, StringComparer.OrdinalIgnoreCase)),
                ("userSessionExit", user.GuestExitCode),
                ("wsb", user.OneLine));
        }

        var tests = job.IsTestRun ? GuestTestResults.Read(Path.Combine(outputFolder, "console.log")) : [];
        var problem = Problem(run, exitCode);
        _timeline.Write("job-end", clock.Elapsed, ("job", job.Name), ("exit", exitCode), ("tests", tests.Count), ("problem", problem));
        return new SandboxJobResult(job, stage, outputFolder, exitCode, run.TimedOut, clock.Elapsed, tests, problem);
    }

    /// <summary>Stops the sandbox, ends this runner's <c>wsb connect</c>, closes the client windows it opened, and checks the list.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_minimizer is not null)
        {
            await _minimizer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        if (_started)
        {
            var stop = await Wsb.RunAsync(CallLimit, CancellationToken.None, "stop", "--id", _id, "--raw");
            _timeline.Write("stop", stop.Elapsed, ("exit", stop.ExitCode), ("wsb", stop.OneLine));
        }

        if (_connect is { HasExited: false })
        {
            // This runner's own wsb connect, by its process object: never a process found by name.
            _connect.Kill(entireProcessTree: true);
        }

        _connect?.Dispose();
        await CloseClientWindowsAsync();
        var running = await Wsb.ListAsync(CancellationToken.None);
        _timeline.Write("list-after", null, ("stillListed", running?.Contains(_id, StringComparer.OrdinalIgnoreCase)), ("running", running?.Count));
        _timeline.Write("session-end", _timeline.Elapsed, ("minimizes", Minimizes), ("jobs", _jobs));
        _timeline.Dispose();
        _stopping.Dispose();
    }

    private async Task StartAsync(SandboxFeatures features, TimeSpan readyLimit, CancellationToken cancellationToken)
    {
        var configuration = GuestScript.Configuration(features, _input, _output);
        await File.WriteAllTextAsync(Path.Combine(_output, "config.xml"), configuration, cancellationToken);

        // From here the id is this runner's: whatever wsb start does, the disposal stops it (a stop of an id never started fails harmlessly).
        _started = true;
        var start = await Wsb.RunAsync(TimeSpan.FromMinutes(5), cancellationToken, "start", "--id", _id, "--config", configuration, "--raw");
        _timeline.Write("start", start.Elapsed, ("exit", start.ExitCode), ("wsb", start.OneLine));
        if (start.ExitCode != 0)
        {
            throw new InvalidOperationException($"Windows Sandbox did not start (wsb start exited {start.ExitCode}): {start.OneLine}");
        }

        await WaitAsync("ready-system", SandboxLane.System, "cmd /c exit 0", readyLimit, cancellationToken);

        // The marker that tells the guest's tests they run in a runner's guest (SandboxGuest), on the guest's own disk.
        var marker = await Wsb.ExecAsync(
            _id,
            SandboxLane.System,
            $@"cmd /c mkdir C:\spar\runner 2>nul & > {SandboxGuest.MarkerPath} echo {_runId}",
            CallLimit,
            cancellationToken);
        _timeline.Write("marker", marker.Elapsed, ("guestExit", marker.GuestExitCode));
        if (marker.GuestExitCode != 0)
        {
            throw new InvalidOperationException($"The runner's marker could not be written in the guest: {marker.OneLine}");
        }

        // The guest's WebView2 registration can name a version whose files it does not have (it takes the files from the
        // host, the registration from its base image): the loader then finds no Runtime. Point it at the version on disk.
        var registration = await Wsb.ExecAsync(_id, SandboxLane.System, GuestScript.WebView2Registration, CallLimit, cancellationToken);
        _timeline.Write("webview2-registration", registration.Elapsed, ("guestExit", registration.GuestExitCode));
    }

    /// <summary>
    /// Opens the guest's user session once: <c>wsb connect</c>, then a command run as the signed-in user. The client's
    /// window is minimized whenever it shows, until the stop.
    /// </summary>
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_connect is not null)
        {
            return;
        }

        lock (_windowsGate)
        {
            foreach (var window in SandboxClientWindows.List())
            {
                _windowsBefore.Add(window.Handle);
            }
        }

        var clock = Stopwatch.StartNew();
        _connect = Wsb.StartConnect(_id);
        _minimizer = Task.Run(() => KeepMinimizedAsync(_stopping.Token), CancellationToken.None);
        await WaitAsync("ready-user-session", SandboxLane.UserSession, "cmd /c exit 0", CallLimit, cancellationToken);
        _timeline.Write("connect", clock.Elapsed, ("connectExited", _connect.HasExited));
    }

    /// <summary>
    /// The client restores and activates itself as the connection completes, after an early minimize, so every new
    /// client window that shows is minimized again, every 100 ms, until the stop. No input: <c>ShowWindowAsync</c>.
    /// </summary>
    private async Task KeepMinimizedAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            foreach (var window in SandboxClientWindows.List())
            {
                bool added;
                lock (_windowsGate)
                {
                    if (_windowsBefore.Contains(window.Handle))
                    {
                        continue;
                    }

                    added = _windowsOurs.Add(window.Handle);
                }

                if (added)
                {
                    _timeline.Write("client-window", null, ("visible", window.Visible), ("foreground", window.Foreground));
                }

                if (window.Visible && !window.Minimized)
                {
                    SandboxClientWindows.Minimize(window.Handle);
                    Minimizes++;
                    _timeline.Write("client-minimize", null, ("wasForeground", window.Foreground));
                }
            }

            try
            {
                await Task.Delay(100, stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>After the stop: closes the client windows that appeared after this runner's connect, and waits until they are gone.</summary>
    private async Task CloseClientWindowsAsync()
    {
        if (_connect is null)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        List<nint> ours;
        lock (_windowsGate)
        {
            foreach (var window in SandboxClientWindows.List())
            {
                if (!_windowsBefore.Contains(window.Handle))
                {
                    _windowsOurs.Add(window.Handle);
                }
            }

            ours = [.. _windowsOurs];
        }

        var left = ours.Count(SandboxClientWindows.Exists);
        foreach (var window in ours.Where(SandboxClientWindows.Exists))
        {
            SandboxClientWindows.Close(window);
        }

        while (ours.Any(SandboxClientWindows.Exists) && clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(50, CancellationToken.None);
        }

        _timeline.Write("close-clients", clock.Elapsed, ("leftAfterStop", left), ("leftAfterClose", ours.Count(SandboxClientWindows.Exists)));
    }

    /// <summary>Copies the payload into a new staging folder (no PDBs), with its guest script and the marker file written last.</summary>
    private string Stage(SandboxJob job)
    {
        var clock = Stopwatch.StartNew();
        var stage = string.Create(CultureInfo.InvariantCulture, $"j{++_jobs:D2}-{Guid.NewGuid().ToString("N")[..6]}");
        var target = Path.Combine(_input, stage);
        var (files, bytes) = Copy(job.PayloadFolder, target);
        File.WriteAllText(Path.Combine(target, "run.cmd"), GuestScript.Job(job, stage, _runId));
        File.WriteAllText(Path.Combine(target, "ready.txt"), stage);
        _timeline.Write("stage", clock.Elapsed, ("job", job.Name), ("stage", stage), ("files", files), ("bytes", bytes));
        return stage;
    }

    private static (int Files, long Bytes) Copy(string from, string to)
    {
        var files = 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
            files++;
            bytes += new FileInfo(file).Length;
        }

        return (files, bytes);
    }

    /// <summary>Waits for <c>exitcode.txt</c>, which the guest script moves into place last; null when it never comes.</summary>
    private async Task<int?> ExitCodeAsync(string outputFolder, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var file = Path.Combine(outputFolder, "exitcode.txt");
        while (!File.Exists(file) && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50, cancellationToken);
        }

        var text = File.Exists(file) ? (await File.ReadAllTextAsync(file, cancellationToken)).Trim() : null;
        _timeline.Write("collect", clock.Elapsed, ("exitFile", text));
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null;
    }

    /// <summary>Copies the logs of the job's data roots (the apps' <c>app.log</c> among them) from the guest's disk to its output.</summary>
    private async Task CollectLogsAsync(string stage, CancellationToken cancellationToken)
    {
        var from = $@"{GuestScript.GuestWork}\{stage}\artifacts";
        var to = $@"{GuestScript.GuestOutput}\{stage}\artifacts";
        var command = $"robocopy {from} {to} *.log /s /njh /njs /nfl /ndl /np";
        var copy = await Wsb.ExecAsync(_id, SandboxLane.System, command, CallLimit, cancellationToken);
        _timeline.Write("collect-logs", copy.Elapsed, ("guestExit", copy.GuestExitCode));
    }

    /// <summary>Polls <paramref name="command"/> until it exits 0 in the guest, at most <paramref name="limit"/>.</summary>
    private async Task WaitAsync(string step, SandboxLane lane, string command, TimeSpan limit, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var attempts = 0;
        WsbResult? last = null;
        while (clock.Elapsed < limit)
        {
            attempts++;
            last = await Wsb.ExecAsync(_id, lane, command, CallLimit, cancellationToken);
            if (last.GuestExitCode == 0)
            {
                _timeline.Write(step, clock.Elapsed, ("attempts", attempts));
                return;
            }

            await Task.Delay(Poll, cancellationToken);
        }

        _timeline.Write(step + "-failed", clock.Elapsed, ("attempts", attempts), ("wsb", last?.OneLine));
        throw new TimeoutException($"The sandbox was not {step} within {limit.TotalSeconds:0} s: {last?.OneLine}");
    }

    private static string? Problem(WsbResult run, int? exitCode)
    {
        if (run.TimedOut)
        {
            return "The run reached its deadline and was ended in the guest.";
        }

        if (exitCode is null)
        {
            return $"No exit code came back from the guest (wsb: {run.OneLine}).";
        }

        if (run.GuestExitCode is { } reported && reported != exitCode)
        {
            return $"wsb reported exit {reported}, the guest script wrote {exitCode}.";
        }

        return null;
    }
}
