using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spar.Devtools;
using Spar.Tool.Project;

namespace Spar.Tool.Dev;

/// <summary>
/// <c>spar dev</c> (plan, M2b; decisions "M2's decide-first questions", item 5, and "M2b's decide-first questions"):
/// Spar's own supervisor of one app's Development runs. It builds, starts the app from a copy of the output, and on
/// each <c>reload</c> builds again: a failed build answers with its diagnostics and keeps the running app; a successful
/// one ends the running app through its session end, starts the new build in the old window's place without the focus,
/// and answers once its page reports <c>ui-ready</c>. Everything it starts is in its job or groups; the development
/// server, which the app reuses through <c>obj/spar-dev-server.json</c>, outlives each run and ends with the session.
/// </summary>
internal sealed class DevSupervisor : IAsyncDisposable
{
    /// <summary>The previous window's place for a restart, which a Development build's Windows host reads (<see cref="WindowKeeper"/>).</summary>
    public const string KeptWindowVariable = "SPAR_DEV_WINDOW";

    /// <summary>How long a reload waits for the new run's <c>ui-ready</c>.</summary>
    public static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(60);

    private readonly AppProject _project;
    private readonly LaunchProfile _profile;
    private readonly PrivateDisplay? _display;
    private readonly IChildProcesses _children;
    private readonly AppBuild _build;
    private readonly TextWriter _output;
    private readonly FileStream _lock;
    private readonly DevtoolsListener _listener;
    private readonly DevFile _file;
    private readonly SemaphoreSlim _reloading = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _accepting;
    private AppRun? _run;
    private int _starts;
    private int _disposed;

    private DevSupervisor(AppProject project, LaunchProfile profile, PrivateDisplay? display, IChildProcesses children, string session, TextWriter output, FileStream lockFile)
    {
        _project = project;
        _profile = profile;
        _display = display;
        _children = children;
        _output = output;
        _lock = lockFile;
        _build = new AppBuild(children, project.File, session, Print);
        _listener = DevtoolsListener.Start(session, "spar-dev-");
        _file = new DevFile(
            Environment.ProcessId, DateTimeOffset.UtcNow, _listener.Transport, _listener.Address,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), profile.Name, display is not null);
        _file.Write(DevFile.PathFor(project.Directory));
        _accepting = Task.Run(AcceptAsync);
    }

    /// <summary>Completes when <c>spar dev --stop</c>, Ctrl+C or a termination signal asks the session to end.</summary>
    public Task Ended => _ended.Task;

    /// <summary>
    /// The session for <paramref name="project"/>, or null when another <c>spar dev</c> holds it (the problem says
    /// where). <paramref name="isPrivate"/> runs the app on a private desktop or Xvfb.
    /// </summary>
    public static async Task<(DevSupervisor? Supervisor, string Problem)> StartAsync(AppProject project, LaunchProfile profile, bool isPrivate, TextWriter output)
    {
        var folder = Path.Combine(project.Directory, "obj");
        Directory.CreateDirectory(folder);
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(Path.Combine(folder, "spar-dev.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            var other = DevFile.Read(DevFile.PathFor(project.Directory));
            var where = other is null ? "" : $" (process {other.ProcessId}, profile {other.Profile}, since {other.Started:u})";
            return (null, $"spar dev already runs for {project.Name}{where}: use spar reload, or spar dev --stop to end it.");
        }

        var session = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        PrivateDisplay? display = null;
        IChildProcesses? children = null;
        try
        {
            if (isPrivate && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                display = PrivateDisplay.CreateDesktop();
            }

            children = IChildProcesses.Create(display?.Desktop);
            if (isPrivate && OperatingSystem.IsLinux())
            {
                display = await PrivateDisplay.StartXvfbAsync(children, CancellationToken.None);
            }

            return (new DevSupervisor(project, profile, display, children, session, output, lockFile), "");
        }
        catch
        {
            children?.Dispose();
            display?.Dispose();
            await lockFile.DisposeAsync();
            throw;
        }
    }

    public string Describe() =>
        $"spar dev: {_project.Name}, launch profile {_profile.Name}{(_display is null ? "" : ", on " + _display.Description)}; "
        + "spar reload builds and restarts it, spar dev --stop or Ctrl+C ends the session.";

    /// <summary>Asks the session to end; <see cref="Ended"/> completes.</summary>
    public void RequestEnd() => _ended.TrySetResult();

    /// <summary>
    /// One reload, one at a time: a request made while another builds waits, then builds again, so its answer always
    /// covers the files as they were when it was asked.
    /// </summary>
    public async Task<ReloadResult> ReloadAsync()
    {
        await _reloading.WaitAsync(_stopping.Token);
        try
        {
            var build = await _build.RunAsync(_stopping.Token);
            Print(build.Succeeded
                ? $"built in {build.Duration.TotalSeconds:0.0} s"
                : $"build failed in {build.Duration.TotalSeconds:0.0} s: {build.Errors.Count()} error(s)");
            var running = _run is { HasExited: false } current ? current : null;
            if (!build.Succeeded)
            {
                return new ReloadResult(build, running is null ? ReloadResult.None : ReloadResult.Kept, running?.ProcessId, running?.Run, null, null, null, [], null);
            }

            return await RestartAsync(build, running);
        }
        finally
        {
            _reloading.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync();
        if (_run is { HasExited: false } run)
        {
            Print($"ending the app (pid {run.ProcessId})…");
            Print("the app " + await run.StopAsync(_project.Directory));
        }

        _children.Dispose();
        _display?.Dispose();
        _file.DeleteIfOurs(DevFile.PathFor(_project.Directory));
        await _listener.DisposeAsync();
        await Task.WhenAny(_accepting, Task.Delay(1000, CancellationToken.None));
        await _lock.DisposeAsync();
        Print("spar dev ended");
    }

    private async Task<ReloadResult> RestartAsync(BuildResult build, AppRun? running)
    {
        var slot = RunCopy.NextSlot(_project.Directory, running?.Folder);
        RunCopy.Sync(build.OutputDirectory!, slot);
        var executable = Path.Combine(slot, Path.GetRelativePath(build.OutputDirectory!, build.Executable!));

        string? place = null;
        if (running is not null && _display is null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            place = WindowKeeper.Read(running.ProcessId);
        }

        var previous = running?.Run;
        if (running is not null)
        {
            Print($"ending the previous run (pid {running.ProcessId})…");
            Print("the previous run " + await running.StopAsync(_project.Directory));
        }

        var start = new ChildStart(executable, _profile.Arguments, WorkingDirectory(), AppEnvironment(place), Window: true, Activate: _starts == 0);
        var run = AppRun.Start(_children, start, Print);
        _run = run;
        _starts++;
        Print($"started {Path.GetFileName(executable)} (pid {run.ProcessId}) from {slot}");
        _ = WatchExitAsync(run);

        var (state, ready, waited) = await run.WaitForStartAsync(StartLimit);
        return state switch
        {
            StartState.Ready => new ReloadResult(build, ReloadResult.Ready, run.ProcessId, run.Run, ready, waited.TotalSeconds, null, [], previous, run.ReadyPhase),
            StartState.Exited => new ReloadResult(build, ReloadResult.Exited, run.ProcessId, null, null, waited.TotalSeconds, await run.Exited, run.LastLines, previous),
            _ => new ReloadResult(build, ReloadResult.Starting, run.ProcessId, null, null, waited.TotalSeconds, null, run.LastLines, previous),
        };
    }

    private async Task WatchExitAsync(AppRun run)
    {
        var code = await run.Exited;
        if (ReferenceEquals(_run, run) && !run.Stopping && !_stopping.IsCancellationRequested)
        {
            Print($"the app (pid {run.ProcessId}) exited with {code}; spar reload builds and starts it again");
        }
    }

    /// <summary>The launch profile's folder (relative to the project), else the project's own, as <c>dotnet run</c>'s profiles read it.</summary>
    private string WorkingDirectory() =>
        _profile.WorkingDirectory is { Length: > 0 } folder ? Path.GetFullPath(Path.Combine(_project.Directory, folder)) : _project.Directory;

    private Dictionary<string, string?> AppEnvironment(string? place)
    {
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in _profile.Environment)
        {
            variables[name] = value;
        }

        if (_display is not null)
        {
            foreach (var (name, value) in _display.Environment)
            {
                variables[name] = value;
            }
        }

        variables["DOTNET_LAUNCH_PROFILE"] = _profile == LaunchProfile.None ? null : _profile.Name;
        variables[KeptWindowVariable] = place;
        return variables;
    }

    private void Print(string line)
    {
        lock (_output)
        {
            _output.WriteLine(line);
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                stream = await _listener.AcceptAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException)
            {
                continue;
            }

            _ = Task.Run(() => ServeAsync(stream));
        }
    }

    private async Task ServeAsync(Stream stream)
    {
        await using var _ = stream;
        var line = new ArrayBufferWriter<byte>();
        var buffer = new byte[16 * 1024];
        var pending = new JsonRpc.Pending();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var initialized = false;
        try
        {
            while (await JsonRpc.ReadLineAsync(stream, line, buffer, pending, _stopping.Token) is { } request)
            {
                if (request.Length == 0)
                {
                    continue;
                }

                var (bytes, close, accepted) = await AnswerAsync(request, ids, initialized);
                initialized |= accepted;
                await stream.WriteAsync(bytes, _stopping.Token);
                await stream.FlushAsync(_stopping.Token);
                if (close)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went, or the session is ending.
        }
    }

    /// <summary>One request's answer: <c>initialize</c> with the token first, then <c>reload</c>, <c>status</c> or <c>stop</c>.</summary>
    private async Task<(byte[] Bytes, bool Close, bool Initialized)> AnswerAsync(byte[] request, HashSet<string> ids, bool initialized)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request);
        }
        catch (JsonException exception)
        {
            return (JsonRpc.Error(null, JsonRpc.ParseError, "The request is not JSON: " + exception.Message, null), false, false);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || !JsonRpc.IsId(id)
                || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            {
                return (JsonRpc.Error(null, JsonRpc.InvalidRequest, "A request is {\"jsonrpc\":\"2.0\",\"id\":…,\"method\":…} on one line.", null), false, false);
            }

            if (!ids.Add(id.GetRawText()))
            {
                return (JsonRpc.Error(id, JsonRpc.ToolError, "This id was used on this connection already; the request was not run again.", "duplicate_id"), false, false);
            }

            var name = method.GetString();
            if (!initialized)
            {
                var token = root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("token", out var given) && given.ValueKind == JsonValueKind.String ? given.GetString() ?? "" : "";
                var accepted = name == "initialize" && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_file.Token));
                return accepted
                    ? (JsonRpc.Result(id, writer => Status(writer)), false, true)
                    : (JsonRpc.Error(id, JsonRpc.ToolError, "The first request is initialize, with the token of obj/spar-dev.json.", "unauthorized"), true, false);
            }

            switch (name)
            {
                case "reload":
                    var result = await ReloadAsync();
                    return (JsonRpc.Result(id, result.Write), false, false);
                case "status":
                    return (JsonRpc.Result(id, Status), false, false);
                case "stop":
                    RequestEnd();
                    return (JsonRpc.Result(id, writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteBoolean("stopping", true);
                        writer.WriteEndObject();
                    }), false, false);
                default:
                    return (JsonRpc.Error(id, JsonRpc.MethodNotFound, $"spar dev has no '{name}': reload, status and stop.", "not_found"), false, false);
            }
        }
    }

    private void Status(Utf8JsonWriter writer)
    {
        var run = _run;
        writer.WriteStartObject();
        writer.WriteNumber("pid", System.Environment.ProcessId);
        writer.WriteString("project", _project.File);
        writer.WriteString("profile", _profile.Name);
        writer.WriteBoolean("private", _display is not null);
        writer.WriteString("app", run is null ? "none" : run.HasExited ? "exited" : "running");
        if (run is not null)
        {
            writer.WriteNumber("appPid", run.ProcessId);
            if (run.Run is { } id)
            {
                writer.WriteString("run", id);
            }
        }

        writer.WriteEndObject();
    }
}
