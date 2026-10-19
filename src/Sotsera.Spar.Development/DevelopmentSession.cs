using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Sotsera.Spar.Development.Interop;
using Sotsera.Spar.Windows;

namespace Sotsera.Spar.Development;

/// <summary>Owns one SPA server and one application, with either an SDK watcher or editor debugger update owner.</summary>
public sealed partial class DevelopmentSession(DevelopmentSettings settings, Action<DevelopmentEvent> observer, bool enableProbe = false,
    Func<DevelopmentDebugLaunch, Task>? launchDebugger = null) : IAsyncDisposable
{
    private readonly object _eventGate = new();
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly string _token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private readonly string _credential = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private readonly string _httpScope = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private readonly Channel<Incoming> _messages = Channel.CreateBounded<Incoming>(128);
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _force = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _startupStop = new();
    private readonly Dictionary<(int Id, long Started), Process> _observed = [];
    private readonly HashSet<(int Id, long Started)> _exited = [];
    private OwnedProcessJob? _job;
    private Child? _build;
    private Child? _frontend;
    private Child? _application;
    private Peer? _frontendPeer;
    private Peer? _applicationPeer;
    private string _directory = "";
    private string _contract = "";
    private string? _currentDocument;
    private string? _readyDocument;
    private string? _restartCorrelation;
    private string? _stopCorrelation;
    private string _stopReason = "owner_stop";
    private Uri? _frontendUri;
    private DateTimeOffset _readyDeadline;
    private bool _applicationControlLost;
    private bool _awaitingInitialReadiness;
    private int _generation = 1;
    private int _started;
    private int _forced;
    private int _disposed;
    private Exception? _observerFailure;

    public void RequestStop(bool force = false, string reason = "owner_stop", string? correlation = null)
    {
        if (_ended.Task.IsCompleted || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        _stopReason = reason;
        _stopCorrelation = correlation;
        _stop.TrySetResult();
        try
        {
            _startupStop.Cancel();
        }
        catch (ObjectDisposedException) { return; }
        if (force)
        {
            _force.TrySetResult();
            if (_job is not null)
            {
                ForceOwnedProcesses("explicit_" + reason);
            }
        }
    }

    public async Task<int> RunAsync(ChannelReader<DevelopmentCommand> commands, bool skipBuild = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A session can run only once.");
        }
        try
        {
            return await RunOwnedAsync(commands, skipBuild, cancellationToken).ConfigureAwait(false);
        }
        finally { _ended.TrySetResult(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _started) != 0 && !_ended.Task.IsCompleted)
        {
            RequestStop();
            await _ended.Task.ConfigureAwait(false);
        }

        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _startupStop.Dispose();
        }
    }

    private async Task<int> RunOwnedAsync(ChannelReader<DevelopmentCommand> commands, bool skipBuild,
        CancellationToken cancellationToken)
    {
        using var stopRegistration = cancellationToken.Register(() => RequestStop(reason: "owner_cancelled"));
        _directory = Path.Combine(settings.SessionDirectory, _session);
        Directory.CreateDirectory(_directory);
        var result = 0;
        var cleanupVerified = false;
        _job = new OwnedProcessJob();
        try
        {
            Emit("session-started", detail: _directory);
            if (settings.DebuggerBackend && (settings.WatchBackend || settings.ContractFile is null))
            {
                throw new SessionFailure("invalid_debugger_settings", "Select one backend update owner and supply a separate contract source path.");
            }
            if (_stop.Task.IsCompleted)
            {
                throw new SessionStoppedException();
            }
            if (settings.DebuggerBackend && launchDebugger is null)
            {
                throw new SessionFailure("debugger_owner_required", "Debugger mode requires an editor launch owner.");
            }
            if (settings.WatchBackend || settings.DebuggerBackend)
            {
                _backendInputs = new BackendInputs(settings);
                if (skipBuild)
                {
                    throw new SessionFailure("watch_build_required", "Watch mode requires a selected full build; omit --no-build.");
                }
            }
            if (!skipBuild && !await BuildSelectedApplicationAsync().ConfigureAwait(false))
            {
                throw new SessionFailure("build_failed", "Initial backend/contracts did not build; inspect build logs.");
            }
            if (_stop.Task.IsCompleted)
            {
                throw new SessionStoppedException();
            }
            ReadContract();
            if (settings.WatchBackend)
            {
                await VerifyWatchSdkAsync().ConfigureAwait(false);
            }
            CheckPort();
            await StartFrontendAsync().ConfigureAwait(false);
            if (!await StartApplicationAsync().ConfigureAwait(false))
            {
                await RebuildApplicationAsync("source_changed_during_start").ConfigureAwait(false);
            }
            await RunLoopAsync(commands).ConfigureAwait(false);
        }
        catch (SessionStoppedException) { }
        catch (OperationCanceledException) when (_stop.Task.IsCompleted) { }
        catch (Exception error)
        {
            result = 1;
            Emit("session-failed", code: error is SessionFailure failure ? failure.Code : "session_error", detail: error.Message);
        }
        finally
        {
            try
            {
                await ShutdownAsync().ConfigureAwait(false);
                cleanupVerified = true;
            }
            catch (Exception error)
            {
                result = 1;
                Emit("cleanup-failed", code: "unfinished_cleanup", detail: error.Message);
            }

            _job.Dispose(); // The last resort remains active through every failure, including owner/process exit.
            DisposeWatchResources();
            foreach (var process in _observed.Values)
            {
                process.Dispose();
            }
            _build?.Process.Dispose();
            _frontend?.Process.Dispose();
            _application?.Process.Dispose();
        }

        Emit("session-stopped", code: !cleanupVerified ? "cleanup_unverified" : Volatile.Read(ref _forced) != 0 ? "forced_cleanup" : "graceful", detail: _stopReason,
            correlation: _stopCorrelation);
        return _observerFailure is null ? result : 1;
    }

    private async Task StartFrontendAsync()
    {
        var name = "spar-frontend-" + Guid.NewGuid().ToString("N");
        var pipe = CreateServer(name);
        var variables = new Dictionary<string, string>(settings.Frontend.Environment, StringComparer.OrdinalIgnoreCase)
        {
            ["SPAR_DEV_FRONTEND_PIPE"] = name,
            ["SPAR_DEV_SESSION"] = _session,
            ["SPAR_DEV_TOKEN"] = _token,
            ["SPAR_DEV_CLIENT_CREDENTIAL"] = _credential,
            ["SPAR_DEV_HTTP_SCOPE"] = _httpScope,
            ["SPAR_DEV_PORT"] = settings.Frontend.Port.ToString(CultureInfo.InvariantCulture),
            ["SPAR_DEV_PROBE"] = enableProbe ? "1" : "0"
        };
        try
        {
            _frontend = Start("frontend", settings.Frontend.FileName, settings.Frontend.Arguments,
                settings.Frontend.WorkingDirectory, variables);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_startupStop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.StartupTimeoutSeconds));
            _frontendPeer = await AwaitStartupAsync(AcceptAsync(pipe, "frontend", timeout.Token), _frontend, timeout,
                "frontend_startup_timeout").ConfigureAwait(false);
            var ready = await AwaitStartupAsync(_frontendPeer.Connection.ReadAsync(timeout.Token), _frontend, timeout,
                "frontend_startup_timeout").ConfigureAwait(false);
            if (ready?.Kind != "frontend-ready" || ready.Token != _token || ready.Session != _session || ready.Url is null)
            {
                throw new SessionFailure("frontend_protocol_error", "The frontend adapter did not announce authenticated readiness.");
            }

            var content = new SparLoopbackContent(new Uri(ready.Url), _credential);
            if (content.BaseUri.AbsolutePath != "/" + _httpScope + "/")
            {
                throw new SessionFailure("frontend_scope_mismatch", "The adapter announced a different session scope.");
            }

            _frontendUri = content.BaseUri;
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = new HttpClient(handler);
            using var response = await client.GetAsync(new Uri(_frontendUri, "__spar_ready"),
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || !response.Headers.TryGetValues("X-Spar-Proof", out var values)
                || !SecretEquals(values.Single(), _credential))
            {
                throw new SessionFailure("frontend_identity_mismatch", "The advertised HTTP server did not prove session ownership.");
            }

            Emit("frontend-ready", "frontend", _frontendPeer.Process, port: _frontendUri.Port, contract: _contract);
            _frontendPeer.StartReading(_messages.Writer, "frontend", 0);
        }
        catch
        {
            if (_frontendPeer is null)
            {
                pipe.Dispose();
            }
            throw;
        }
    }

    private async Task<bool> StartApplicationAsync()
    {
        if (_stop.Task.IsCompleted)
        {
            throw new SessionStoppedException();
        }
        _currentDocument = null;
        _readyDocument = null;
        _applicationControlLost = false;
        _awaitingInitialReadiness = true;
        _lastBackendRevision = 0;
        _pendingUpdate = 0;
        var name = "spar-app-" + Guid.NewGuid().ToString("N");
        var pipe = CreateServer(name);
        try
        {
            var variables = new Dictionary<string, string>
            {
                ["SPAR_DEV_APP_PIPE"] = name,
                ["SPAR_DEV_SESSION"] = _session,
                ["SPAR_DEV_TOKEN"] = _token
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_startupStop.Token);
            if (settings.DebuggerBackend)
            {
                _applicationPeer = await LaunchDebuggerAsync(pipe, variables).ConfigureAwait(false);
            }
            else
            {
                var launcher = settings.WatchBackend
                    ? StartWatcher(variables)
                    : Start("application", settings.Application, ["--spar-dev"], Path.GetDirectoryName(settings.Application)!, variables);
                if (!settings.WatchBackend)
                {
                    _application = launcher;
                }
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.StartupTimeoutSeconds));
                _applicationPeer = await AwaitStartupAsync(AcceptAsync(pipe, "app", timeout.Token), launcher, timeout,
                    "application_startup_timeout").ConfigureAwait(false);
            }
            if (settings.WatchBackend || settings.DebuggerBackend)
            {
                var application = Process.GetProcessById(_applicationPeer.Process.Id);
                _ = application.Handle;
                _application = new Child(application, "application", application.WaitForExitAsync());
                Emit("process-started", "application", application);
                ReadContract(); // The SDK's initial build must agree with the generated files it just produced.
            }
            if (!SelectionIsCurrent())
            {
                Emit("build-superseded", code: "before_admission", detail: _selectedInputs?.Hash);
                _applicationPeer.StartReading(_messages.Writer, "application", _generation);
                return false;
            }
            if (_applicationPeer.Hello.Contract != _contract)
            {
                throw new SessionFailure("contract_mismatch", "The compiled backend and generated frontend contract do not match.");
            }

            await _applicationPeer.Connection.WriteAsync(new SessionMessage("configure", _token, _session,
                Url: _frontendUri!.AbsoluteUri, Contract: _contract, Credential: _credential,
                Profile: Path.Combine(_directory, "profile-" + _generation.ToString(CultureInfo.InvariantCulture)),
                Generation: _generation), timeout.Token).ConfigureAwait(false);
            _applicationPeer.StartReading(_messages.Writer, "application", _generation);
            _readyDeadline = DateTimeOffset.UtcNow.AddSeconds(settings.StartupTimeoutSeconds);
            return true;
        }
        catch
        {
            if (_applicationPeer is null)
            {
                pipe.Dispose();
            }
            throw;
        }
    }

    private async Task RunLoopAsync(ChannelReader<DevelopmentCommand> commands)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        var tick = timer.WaitForNextTickAsync().AsTask();
        var messages = _messages.Reader.WaitToReadAsync().AsTask();
        Task<bool>? command = commands.WaitToReadAsync().AsTask();
        while (!_stop.Task.IsCompleted)
        {
            if (_frontend!.Exited.IsCompleted || _frontendPeer!.Reader!.IsCompleted)
            {
                throw new SessionFailure("frontend_exited", "The owned frontend ended; the application is being stopped.");
            }

            if (_application?.Exited.IsCompleted == true)
            {
                Emit("application-exited", "application", _application.Process, code: _application.Process.ExitCode.ToString(CultureInfo.InvariantCulture));
                if (_application.Process.ExitCode != 0)
                {
                    _stopReason = "application_failed";
                    throw new SessionFailure("application_exited", "The owned application exited unexpectedly; inspect application stdout/stderr logs.");
                }

                _stopReason = "application_closed";
                return;
            }

            if (_watcher?.Exited.IsCompleted == true)
            {
                throw new SessionFailure("watcher_exited", "The backend update owner exited; inspect its retained logs.");
            }

            if (settings.DebuggerBackend && Volatile.Read(ref _debuggerPaused) != 0)
            {
                _readyDeadline = DateTimeOffset.UtcNow.AddSeconds(settings.StartupTimeoutSeconds);
            }
            if (_application is not null && _readyDocument is null && DateTimeOffset.UtcNow > _readyDeadline)
            {
                if (_applicationControlLost)
                {
                    throw new SessionFailure("application_disconnected", "Application control ended without confirmed process exit.");
                }

                throw new SessionFailure("application_ready_timeout", "The native application did not confirm rendered DOM and a typed call.");
            }

            if (tick.IsCompleted)
            {
                await tick.ConfigureAwait(false);
                TrackProcesses();
                if (InputRestartReason() is { } reason)
                {
                    await RebuildApplicationAsync(reason).ConfigureAwait(false);
                }
                else if (ReadWatchOutput())
                {
                    await RebuildApplicationAsync("unsupported_edit").ConfigureAwait(false);
                }
                await PublishWatchDiagnosticAsync().ConfigureAwait(false);
                tick = timer.WaitForNextTickAsync().AsTask();
            }

            // Source selection takes precedence over queued readiness or update frames.
            if (_backendInputs is not null && _application is not null && _awaitingInitialReadiness && !SelectionIsCurrent())
            {
                await RebuildApplicationAsync("source_changed_during_start").ConfigureAwait(false);
            }
            while (_messages.Reader.TryRead(out var incoming))
            {
                HandleMessage(incoming);
            }
            if (_contractRestartPending)
            {
                _contractRestartPending = false;
                await RebuildApplicationAsync("contract_update").ConfigureAwait(false);
            }
            await PublishBackendUpdateAsync().ConfigureAwait(false);
            while (commands.TryRead(out var request))
            {
                Emit("command-accepted", correlation: request.Id, detail: request.Command);
                if (request.Command == "status")
                {
                    TrackProcesses();
                    Emit("command-completed", code: _application is null ? "stopped" : _readyDocument is null ? "starting" : "ready",
                        correlation: request.Id);
                }
                else if (request.Command == "restart")
                {
                    _restartCorrelation = request.Id;
                    if (settings.WatchBackend || settings.DebuggerBackend)
                    {
                        await RebuildApplicationAsync("owner_restart").ConfigureAwait(false);
                    }
                    else
                    {
                        await StopApplicationAsync().ConfigureAwait(false);
                        if (_force.Task.IsCompleted)
                        {
                            throw new SessionStoppedException();
                        }
                        _generation++;
                        await StartApplicationAsync().ConfigureAwait(false);
                    }
                }
                else if (request.Command == "debugger-diagnostic" && settings.DebuggerBackend
                    && request.Generation == _generation && request.Detail is not null)
                {
                    _pendingDiagnostic = request.Detail.Length > 2048 ? request.Detail[..2048] : request.Detail;
                    Emit("debugger-diagnostic", detail: _pendingDiagnostic);
                    await PublishWatchDiagnosticAsync().ConfigureAwait(false);
                }
                else if (request.Command == "probe" && enableProbe)
                {
                    await _frontendPeer.Connection.WriteAsync(new SessionMessage("probe", Detail: request.Detail)).ConfigureAwait(false);
                    Emit("command-completed", code: "probe-dispatched", correlation: request.Id);
                }
                else
                {
                    Emit("command-rejected", code: "unknown_command", correlation: request.Id);
                }
            }

            if (messages.IsCompleted)
            {
                messages = _messages.Reader.WaitToReadAsync().AsTask();
            }
            if (command?.IsCompleted == true)
            {
                command = await command.ConfigureAwait(false) ? commands.WaitToReadAsync().AsTask() : null;
            }

            var wait = new List<Task> { tick, messages, _frontend.Exited, _frontendPeer.Reader, _stop.Task };
            if (_application is not null)
            {
                wait.Add(_application.Exited);
            }
            if (_watcher is not null)
            {
                wait.Add(_watcher.Exited);
            }
            if (command is not null)
            {
                wait.Add(command);
            }
            await Task.WhenAny(wait).ConfigureAwait(false);
        }
    }

    private void HandleMessage(Incoming incoming)
    {
        if (incoming.Role == "frontend")
        {
            throw new SessionFailure("frontend_disconnected", "The frontend adapter's ownership connection ended.");
        }

        if (incoming.Generation != _generation)
        {
            return;
        }
        var message = incoming.Message;
        if (message is null)
        {
            // EOF is not process exit. Withdraw readiness while retaining ownership until actual completion.
            _applicationControlLost = true;
            _currentDocument = null;
            _readyDocument = null;
            _readyDeadline = DateTimeOffset.UtcNow.AddSeconds(settings.ShutdownTimeoutSeconds);
            Emit("application-not-ready", code: "control_disconnected");
            return;
        }
        if (message.Kind == "app-window")
        {
            Emit("window-created", "application", _application!.Process, window: message.Window);
        }
        else if (message.Kind == "app-bridge")
        {
            _currentDocument = message.Document;
            _readyDocument = null;
            _readyDeadline = DateTimeOffset.UtcNow.AddSeconds(settings.StartupTimeoutSeconds);
            Emit("bridge-connected", contract: message.Contract, document: message.Document);
        }
        else if (message.Kind == "app-diagnostic")
        {
            Emit("bridge-diagnostic", code: message.Detail, document: message.Document);
            if (message.Detail == "navigation_denied")
            {
                _currentDocument = null;
                _readyDocument = null;
                _readyDeadline = DateTimeOffset.UtcNow.AddSeconds(settings.StartupTimeoutSeconds);
                Emit("application-not-ready", code: "navigation_denied");
            }
            if (message.Detail == "client_unauthorized")
            {
                throw new SessionFailure("client_unauthorized", "The document did not prove the session's client capability.");
            }
        }
        else if (message.Kind == "app-update" && message.Contract == _contract && message.UpdateRevision is { } revision)
        {
            if (revision <= _lastBackendRevision)
            {
                return;
            }

            _lastBackendRevision = revision;
            Emit("backend-updated", "application", _application!.Process, contract: _contract,
                document: _currentDocument, updateRevision: revision);
            _pendingUpdate = revision;
        }
        else if (message.Kind == "app-contract-update" && message.Contract == _contract)
        {
            _contractRestartPending = true;
            Emit("contract-update-observed", contract: _contract, document: _currentDocument,
                updateRevision: message.UpdateRevision);
        }
        else if (message.Kind == "app-activity")
        {
            Emit("backend-activity", "application", _application!.Process, detail: message.Detail,
                document: _currentDocument);
        }
        else if (message.Kind == "app-ui" && message.Contract == _contract && message.Document == _currentDocument
            && _currentDocument is not null)
        {
            if (_readyDocument is null)
            {
                _readyDocument = message.Document;
                _awaitingInitialReadiness = false;
                Emit("application-ready", "application", _application!.Process, contract: _contract, document: _readyDocument);
                if (_restartCorrelation is not null)
                {
                    Emit("command-completed", code: "restarted", correlation: _restartCorrelation);
                    _restartCorrelation = null;
                }
            }

            Emit("ui-snapshot", "application", _application!.Process, detail: message.Detail, document: message.Document);
        }
    }

    private async Task StopApplicationAsync()
    {
        if (_application is null)
        {
            return;
        }
        _currentDocument = null;
        _readyDocument = null;
        if (!_application.Exited.IsCompleted)
        {
            Emit("application-stop-requested", "application", _application.Process);
            if (_applicationPeer is not null)
            {
                try
                {
                    await _applicationPeer.Connection.WriteAsync(new SessionMessage("stop")).ConfigureAwait(false);
                }
                catch (IOException) { }
            }
            else
            {
                ForceOwnedProcesses("application_startup_incomplete");
            }
        }

        // No grace timeout authorizes dropping application work or starting a replacement.
        await DrainApplicationExitAsync().ConfigureAwait(false);

        Emit("application-stopped", "application", _application.Process);
        if (_applicationPeer is not null)
        {
            await _applicationPeer.DisposeAsync().ConfigureAwait(false);
            _applicationPeer = null;
        }
        _application.Process.Dispose();
        _application = null;
        await StopWatcherAsync().ConfigureAwait(false);
    }

    private async Task ShutdownAsync()
    {
        Emit("session-stopping", code: _stopReason, correlation: _stopCorrelation);
        await StopApplicationAsync().ConfigureAwait(false);
        await StopWatcherAsync().ConfigureAwait(false);
        if (_frontendPeer is not null && !_frontend!.Exited.IsCompleted)
        {
            try
            {
                await _frontendPeer.Connection.WriteAsync(new SessionMessage("stop")).ConfigureAwait(false);
            }
            catch (IOException) { }
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(settings.ShutdownTimeoutSeconds);
        while (_job!.ActiveProcessIds().Length != 0 && DateTimeOffset.UtcNow < deadline && !_force.Task.IsCompleted)
        {
            TrackProcesses();
            await Task.Delay(100).ConfigureAwait(false);
        }

        if (_job.ActiveProcessIds().Length != 0)
        {
            ForceOwnedProcesses("owned_helpers_remaining");
        }
        while (_job.ActiveProcessIds().Length != 0)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
        if (_build is not null)
        {
            await _build.Exited.ConfigureAwait(false);
        }
        if (_frontend is not null)
        {
            await _frontend.Exited.ConfigureAwait(false);
        }
        if (_frontendPeer is not null)
        {
            await _frontendPeer.DisposeAsync().ConfigureAwait(false);
            _frontendPeer = null;
        }
        TrackProcesses();
        Emit("owned-processes-ended");
    }

    private void ForceOwnedProcesses(string code)
    {
        if (Interlocked.Exchange(ref _forced, 1) != 0)
        {
            return;
        }
        Emit("forced-cleanup-requested", code: code);
        try
        {
            _job!.Terminate();
        }
        catch (ObjectDisposedException) { /* Closing the non-inherited handle already requested kernel cleanup. */ }
    }

    private async Task<T> AwaitStartupAsync<T>(Task<T> work, Child child, CancellationTokenSource timeout, string timeoutCode)
    {
        var winner = await Task.WhenAny(work, child.Exited, _stop.Task).ConfigureAwait(false);
        if (winner != work)
        {
            await timeout.CancelAsync().ConfigureAwait(false);
            try
            {
                var abandonedResult = await work.ConfigureAwait(false);
                if (abandonedResult is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception) { }
            if (_stop.Task.IsCompleted)
            {
                throw new SessionStoppedException();
            }
            throw new SessionFailure(child.Role + "_start_failed", "The process exited before readiness; inspect its stdout/stderr logs.");
        }

        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (_stop.Task.IsCompleted)
            {
                throw new SessionStoppedException();
            }
            throw new SessionFailure(timeoutCode, "The adapter did not establish readiness before the startup deadline.");
        }
    }

    private async Task<Peer> AcceptAsync(NamedPipeServerStream pipe, string role, CancellationToken cancellationToken)
    {
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SessionConnection(pipe);
        Process? process = null;
        try
        {
            var hello = await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!ProcessNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var nativeId))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            process = Process.GetProcessById((int)nativeId);
            _ = process.Handle;
            if (hello?.Kind == role + "-hello" && hello.ProcessId == (int)nativeId && hello.Session == _session
                && SecretEquals(hello.Token, _token))
            {
                if (role == "app" && settings.DebuggerBackend && !_job!.Contains(process))
                {
                    AdoptDebuggerProcess(process);
                }
                if (_job!.Contains(process))
                {
                    return new Peer(connection, process, hello);
                }
            }

            Emit("control-rejected", role, code: "peer_unauthorized");
            throw new SessionFailure("control_peer_unauthorized", "The control peer did not prove session ownership.");
        }
        catch
        {
            process?.Dispose();
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Child Start(string role, string executable, IReadOnlyList<string> arguments, string directory,
        IReadOnlyDictionary<string, string> variables, OwnedProcessJob? job = null)
    {
        var stem = role + "-" + _generation.ToString(CultureInfo.InvariantCulture);
        Process process;
        try
        {
            process = (job ?? _job!).Start(executable, arguments, directory, variables,
                Path.Combine(_directory, stem + ".stdout.log"), Path.Combine(_directory, stem + ".stderr.log"));
        }
        catch (Exception error) when (error is IOException or Win32Exception or ArgumentException)
        {
            throw new SessionFailure(role + "_launch_failed", error.Message);
        }
        Emit("process-started", role, process);
        return new Child(process, role, process.WaitForExitAsync());
    }

    private void TrackProcesses()
    {
        foreach (var id in _job!.ActiveProcessIds())
        {
            try
            {
                var process = Process.GetProcessById(id);
                _ = process.Handle;
                var key = (id, process.StartTime.ToUniversalTime().Ticks);
                if (!_job.Contains(process) || !_observed.TryAdd(key, process))
                {
                    process.Dispose();
                    continue;
                }
                if (_observed.Count > 4096)
                {
                    throw new SessionFailure("process_history_limit", "The session exceeded 4096 observed process identities.");
                }
                Emit("process-observed", "owned", process);
            }
            catch (ArgumentException) { }
        }

        foreach (var pair in _observed)
        {
            if (pair.Value.HasExited && _exited.Add(pair.Key))
            {
                Emit("process-exited", "owned", pair.Value);
            }
        }
    }

    private void CheckPort()
    {
        if (settings.Frontend.Port == 0)
        {
            return;
        }
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        try
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, settings.Frontend.Port));
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new SessionFailure("frontend_port_conflict", "The configured loopback port is already in use; choose another or use port 0.");
        }
    }

    private static NamedPipeServerStream CreateServer(string name)
    {
        return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
    }

    private static bool SecretEquals(string? value, string expected)
    {
        return value?.Length == expected.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(expected));
    }

    private void Emit(string kind, string? role = null, Process? process = null, string? code = null, string? detail = null,
        long? window = null, int? port = null, string? contract = null, string? document = null, string? correlation = null,
        long? updateRevision = null)
    {
        lock (_eventGate)
        {
            try
            {
                observer(new DevelopmentEvent(kind, _session, _generation, DateTimeOffset.UtcNow, Environment.ProcessId,
                    role, process?.Id, process?.StartTime.ToUniversalTime(), code, detail, window, port, contract, document,
                    correlation, updateRevision, _selectedInputs?.Hash, _selectedInputs?.Revision));
            }
            catch (Exception error)
            {
                _observerFailure ??= error;
                RequestStop(true, "owner_output_closed");
            }
        }
    }

    private sealed record Child(Process Process, string Role, Task Exited);
    private sealed record Incoming(string Role, int Generation, SessionMessage? Message);
    private sealed class SessionStoppedException : Exception;
    private sealed class SessionFailure(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }

    private sealed class Peer(SessionConnection connection, Process process, SessionMessage hello) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        internal SessionConnection Connection => connection;
        internal Process Process => process;
        internal SessionMessage Hello => hello;
        internal Task? Reader
        {
            get; private set;
        }

        internal void StartReading(ChannelWriter<Incoming> messages, string role, int generation)
        {
            Reader = PumpAsync(messages, role, generation);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            if (Reader is not null)
            {
                await Reader.ConfigureAwait(false);
            }
            process.Dispose();
            _stop.Dispose();
        }

        private async Task PumpAsync(ChannelWriter<Incoming> messages, string role, int generation)
        {
            try
            {
                while (await connection.ReadAsync(_stop.Token).ConfigureAwait(false) is { } message)
                {
                    await messages.WriteAsync(new Incoming(role, generation, message), _stop.Token).ConfigureAwait(false);
                }

                await messages.WriteAsync(new Incoming(role, generation, null), _stop.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException
                or System.Text.Json.JsonException or DecoderFallbackException)
            {
                if (!_stop.IsCancellationRequested)
                {
                    await messages.WriteAsync(new Incoming(role, generation, null), _stop.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
