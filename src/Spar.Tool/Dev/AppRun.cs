using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Spar.Tool.Devtools;

namespace Spar.Tool.Dev;

/// <summary>How a run's start came out: ready (its <c>ui-ready</c> line), exited, or still starting at the limit.</summary>
internal enum StartState
{
    Ready,
    Exited,
    Starting,
}

/// <summary>One run of the app that <c>spar dev</c> started from a copy of the build's output.</summary>
internal sealed partial class AppRun
{
    /// <summary>How many of the app's last output lines a failed start reports.</summary>
    private const int KeptLines = 40;

    private readonly IChild _child;
    private readonly Queue<string> _last = new();
    private readonly TaskCompletionSource<(string Run, string Phase, double Milliseconds)> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _started = Stopwatch.GetTimestamp();

    private AppRun(IChildProcesses children, ChildStart start, Action<string> print)
    {
        Folder = Path.GetDirectoryName(start.Executable)!;
        _child = children.Start(start, (line, _) => OnLine(line, print));
    }

    public int ProcessId => _child.ProcessId;

    public string Folder { get; }

    public Task<int> Exited => _child.Exited;

    public bool HasExited => _child.Exited.IsCompleted;

    /// <summary>Whether <see cref="StopAsync"/> ended it, so its exit was asked for.</summary>
    public bool Stopping { get; private set; }

    /// <summary>The run's id from its <c>ui-ready</c> line, once it came.</summary>
    public string? Run => _ready.Task.IsCompletedSuccessfully ? _ready.Task.Result.Run : null;

    /// <summary>The phase that made it ready: <c>ui-ready</c>, or <c>loopback listening</c> for a page in a browser.</summary>
    public string? ReadyPhase => _ready.Task.IsCompletedSuccessfully ? _ready.Task.Result.Phase : null;

    public IReadOnlyList<string> LastLines
    {
        get
        {
            lock (_last)
            {
                return [.. _last];
            }
        }
    }

    public static AppRun Start(IChildProcesses children, ChildStart start, Action<string> print) => new(children, start, print);

    /// <summary>Waits for the run's <c>ui-ready</c> line, its exit, or <paramref name="limit"/>.</summary>
    public async Task<(StartState State, double ReadyMilliseconds, TimeSpan Waited)> WaitForStartAsync(TimeSpan limit)
    {
        await Task.WhenAny(_ready.Task, _child.Exited, Task.Delay(limit, CancellationToken.None));
        var waited = Stopwatch.GetElapsedTime(_started);
        if (_ready.Task.IsCompletedSuccessfully)
        {
            return (StartState.Ready, _ready.Task.Result.Milliseconds, waited);
        }

        return (_child.Exited.IsCompleted ? StartState.Exited : StartState.Starting, 0, waited);
    }

    /// <summary>
    /// Ends the run (decision "M2b's decide-first questions", item 3): the devtools' <c>stop</c>, which runs the host's
    /// own session end, then a kill of the app's process if it is still there after the session end's budget and a
    /// margin; at once when the build has no devtools. Says how it ended.
    /// </summary>
    public async Task<string> StopAsync(string projectDirectory)
    {
        Stopping = true;
        if (HasExited)
        {
            return "had exited";
        }

        if (await AskToStopAsync(projectDirectory))
        {
            var ended = await Task.WhenAny(_child.Exited, Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None));
            if (ended == _child.Exited)
            {
                return "ended through its session end";
            }
        }

        _child.Kill();
        await Task.WhenAny(_child.Exited, Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));
        return "killed";
    }

    private async Task<bool> AskToStopAsync(string projectDirectory)
    {
        if (DevtoolsTarget.Find(projectDirectory, out _) is not { } target || target.ProcessId != ProcessId)
        {
            return false;
        }

        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var connection = await DevtoolsConnection.OpenAsync(new RealDevtoolsTransport(), target, limit.Token);
            using var answer = await connection.RequestAsync("stop", _ => { }, limit.Token);
            return answer.RootElement.TryGetProperty("result", out _);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or TimeoutException or DevtoolsCallException
                                             or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            // The session end may have closed the socket before the answer came: the exit below tells.
            return !HasExited;
        }
    }

    private void OnLine(string line, Action<string> print)
    {
        print(line);
        lock (_last)
        {
            _last.Enqueue(line);
            if (_last.Count > KeptLines)
            {
                _last.Dequeue();
            }
        }

        if (!_ready.Task.IsCompleted && ReadyLine().Match(line) is { Success: true } match
            && double.TryParse(match.Groups["ms"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
        {
            _ready.TrySetResult((match.Groups["run"].Value, match.Groups["phase"].Value, milliseconds));
        }
    }

    /// <summary>
    /// The host's ready phase on standard error (<c>Spar.Hosting.HostPhases</c>, a format Spar keeps stable):
    /// <c>spar info +412.5 ms: [0f3a9c21] ui-ready at 412.5 ms</c>, the page's bridge connected; on loopback, whose page is
    /// in a browser <c>spar dev</c> does not open, <c>loopback listening</c>. The number may carry the machine's decimal comma.
    /// </summary>
    [GeneratedRegex(@"^spar info \+[\d.,]+ ms: \[(?<run>[0-9a-f]{8})\] (?<phase>ui-ready|loopback listening) at (?<ms>[\d.,]+) ms$", RegexOptions.CultureInvariant)]
    private static partial Regex ReadyLine();
}
