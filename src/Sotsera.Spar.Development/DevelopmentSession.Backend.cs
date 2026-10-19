using System.Globalization;
using System.Text.RegularExpressions;

namespace Sotsera.Spar.Development;

public sealed partial class DevelopmentSession
{
    private const string SupportedWatchSdk = "10.0.402";
    private static readonly Regex _compilerDiagnostic = new(@"\berror [A-Z]+\d+\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private OwnedProcessJob? _watchJob;
    private Child? _watcher;
    private WatchOutput? _watchStdout;
    private WatchOutput? _watchStderr;
    private string? _pendingDiagnostic;
    private long _pendingUpdate;
    private long _lastBackendRevision;
    private bool _watchReady;
    private bool _contractRestartPending;
    private BackendInputs? _backendInputs;
    private BackendSelection? _selectedInputs;
    private BackendSelection? _observedInputs;

    private async Task VerifyWatchSdkAsync()
    {
        _build?.Process.Dispose();
        _build = Start("sdk-check", "dotnet", ["--version"], Path.GetDirectoryName(settings.Project)!,
            new Dictionary<string, string>());
        await _build.Exited.WaitAsync(TimeSpan.FromSeconds(settings.StartupTimeoutSeconds), _startupStop.Token)
            .ConfigureAwait(false);
        var version = File.ReadAllText(Path.Combine(_directory,
            "sdk-check-" + _generation.ToString(CultureInfo.InvariantCulture) + ".stdout.log")).Trim();
        if (_build.Process.ExitCode != 0 || version != SupportedWatchSdk)
        {
            throw new SessionFailure("watch_sdk_not_verified", $"Backend watch mode requires the verified SDK {SupportedWatchSdk}.");
        }

        Emit("backend-owner", code: "dotnet-watch", detail: version);
    }

    private async Task<BackendSelection> SelectInputsAsync()
    {
        while (true)
        {
            _startupStop.Token.ThrowIfCancellationRequested();
            var selection = _backendInputs!.Capture();
            await Task.Delay(300, _startupStop.Token).ConfigureAwait(false);
            if (selection is not null && selection == _backendInputs.Capture())
            {
                return selection;
            }
        }
    }

    private bool SelectionIsCurrent()
    {
        return _backendInputs is null || _selectedInputs is not null && _selectedInputs == _backendInputs.Capture();
    }

    private string? InputRestartReason()
    {
        if (_backendInputs is null)
        {
            return null;
        }
        var current = _backendInputs.Capture();
        if (current is null)
        {
            return _application is not null && _awaitingInitialReadiness ? "source_changed_during_start" : null;
        }
        var previous = _observedInputs;
        _observedInputs = current;
        if (previous is null || current == previous)
        {
            return null;
        }
        if (_application is null)
        {
            return "stopped_source_corrected";
        }
        if (current.ContractHash != previous.ContractHash)
        {
            Emit("contract-source-changed");
            return "contract_source_changed";
        }
        if (current.RestartHash != previous.RestartHash)
        {
            return "startup_or_dependency_changed";
        }
        return _awaitingInitialReadiness ? "source_changed_during_start" : null;
    }

    private async Task<bool> BuildSelectedApplicationAsync()
    {
        while (true)
        {
            if (_backendInputs is not null)
            {
                _selectedInputs = await SelectInputsAsync().ConfigureAwait(false);
                _observedInputs = _selectedInputs;
                Emit("source-selected", detail: _selectedInputs.Hash);
            }
            _build?.Process.Dispose();
            _build = Start("build", "dotnet", ["build", settings.Project, "--disable-build-servers"],
                Path.GetDirectoryName(settings.Project)!, new Dictionary<string, string>());
            if (await Task.WhenAny(_build.Exited, _stop.Task).ConfigureAwait(false) != _build.Exited)
            {
                throw new SessionStoppedException();
            }
            await _build.Exited.ConfigureAwait(false);
            if (!SelectionIsCurrent())
            {
                Emit("build-superseded", code: _build.Process.ExitCode == 0 ? "build_succeeded" : "build_failed",
                    detail: _selectedInputs?.Hash);
                _generation++;
                continue;
            }
            if (_build.Process.ExitCode != 0)
            {
                PublishBuildDiagnostics();
                Emit("backend-stopped", code: "build_failed",
                    detail: "Edit not applied. Correct the source to rebuild automatically; inspect build logs.",
                    correlation: _restartCorrelation);
                if (_restartPending)
                {
                    Emit("command-completed", code: "build_failed", correlation: _restartCorrelation);
                    _restartCorrelation = null;
                    _restartPending = false;
                }
                return false;
            }
            Emit("build-selected", detail: _selectedInputs?.Hash);
            ReadContract();
            return true;
        }
    }

    private void PublishBuildDiagnostics()
    {
        foreach (var suffix in new[] { ".stdout.log", ".stderr.log" })
        {
            var path = Path.Combine(_directory, "build-" + _generation.ToString(CultureInfo.InvariantCulture) + suffix);
            using var output = new WatchOutput(path);
            // Bounded diagnostic preview; the complete output remains in the generation's logs.
            foreach (var line in output.ReadAvailable().Where(line => _compilerDiagnostic.IsMatch(line)).Take(16))
            {
                Emit("build-diagnostic", code: "compiler_error", detail: line.Length > 2048 ? line[..2048] : line);
            }
        }
    }

    private Child StartWatcher(IReadOnlyDictionary<string, string> applicationVariables)
    {
        _pendingUpdate = 0;
        _lastBackendRevision = 0;
        _pendingDiagnostic = null;
        _watchReady = false;
        _contractRestartPending = false;
        _watchJob = new OwnedProcessJob(_job!);
        var variables = new Dictionary<string, string>(applicationVariables)
        {
            ["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "0",
            ["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1",
            ["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1",
            ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1",
            ["DOTNET_WATCH_LOG_LEVEL"] = "Debug",
            ["DOTNET_CLI_UI_LANGUAGE"] = "en-US",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };
        // Keep prompting enabled with redirected input. Never confirm a restart in the SDK: Spar drains first.
        _watcher = Start("watcher", "dotnet",
            ["watch", "--project", settings.Project, "--verbose", "run", "--no-launch-profile",
                "--disable-build-servers", "--property:UseSharedCompilation=false",
                "--property:HotReloadAutoRestart=false", "--", "--spar-dev"],
            Path.GetDirectoryName(settings.Project)!, variables, _watchJob);
        var stem = "watcher-" + _generation.ToString(CultureInfo.InvariantCulture);
        _watchStdout = new WatchOutput(Path.Combine(_directory, stem + ".stdout.log"));
        _watchStderr = new WatchOutput(Path.Combine(_directory, stem + ".stderr.log"));
        return _watcher;
    }

    private bool ReadWatchOutput()
    {
        if (_watcher is null)
        {
            return false;
        }

        var restart = false;
        foreach (var line in _watchStdout!.ReadAvailable().Concat(_watchStderr!.ReadAvailable()))
        {
            if (!_watchReady && line.Contains("dotnet watch", StringComparison.Ordinal)
                && line.Contains("Waiting for changes", StringComparison.Ordinal))
            {
                _watchReady = true;
                Emit("watcher-ready", "watcher", _watcher.Process);
            }
            if (_compilerDiagnostic.IsMatch(line))
            {
                _pendingDiagnostic = line.Length > 2048 ? line[..2048] : line;
                Emit("watcher-diagnostic", code: "compiler_error", detail: _pendingDiagnostic);
            }
            if (line.TrimStart().StartsWith("? Do you want to restart your app?", StringComparison.Ordinal))
            {
                restart = true;
                Emit("restart-required", code: "unsupported_edit");
            }
        }

        return restart;
    }

    private async Task PublishWatchDiagnosticAsync()
    {
        if (_pendingDiagnostic is null)
        {
            return;
        }

        var diagnostic = _pendingDiagnostic;
        _pendingDiagnostic = null;
        await _frontendPeer!.Connection.WriteAsync(new SessionMessage("backend-status", Detail: diagnostic,
            Contract: _contract, Document: _currentDocument, Generation: _generation)).ConfigureAwait(false);
    }

    private async Task PublishBackendUpdateAsync()
    {
        if (_pendingUpdate == 0 || _readyDocument is null)
        {
            return;
        }

        var revision = _pendingUpdate;
        _pendingUpdate = 0;
        await _frontendPeer!.Connection.WriteAsync(new SessionMessage("backend-update", Contract: _contract,
            Document: _readyDocument, Generation: _generation, UpdateRevision: revision)).ConfigureAwait(false);
        Emit("backend-update-published", contract: _contract, document: _readyDocument, updateRevision: revision);
    }

    private async Task RebuildApplicationAsync(string reason)
    {
        if (projectApplication is not null)
        {
            Emit("restart-required", code: reason, detail: "This project launch ends here. Rebuild and launch again in the IDE.");
            RequestStop(reason: "project_restart_required");
            throw new SessionStoppedException();
        }
        Emit("backend-restarting", code: reason, correlation: _restartCorrelation);
        await StopApplicationAsync().ConfigureAwait(false);
        if (_stop.Task.IsCompleted)
        {
            throw new SessionStoppedException();
        }
        if (_frontend!.Exited.IsCompleted || _frontendPeer!.Reader!.IsCompleted)
        {
            throw new SessionFailure("frontend_exited", "The frontend ended while the backend was retiring.");
        }

        _pendingUpdate = 0;
        _pendingDiagnostic = null;
        do
        {
            _generation++;
            if (!await BuildSelectedApplicationAsync().ConfigureAwait(false))
            {
                return;
            }
            if (settings.WatchBackend)
            {
                await VerifyWatchSdkAsync().ConfigureAwait(false);
            }
            if (await StartApplicationAsync().ConfigureAwait(false))
            {
                return;
            }
            await StopApplicationAsync().ConfigureAwait(false);
        } while (!_stop.Task.IsCompleted);
        throw new SessionStoppedException();
    }

    private async Task StopWatcherAsync()
    {
        if (_watchJob is null)
        {
            return;
        }
        if (_application is not null && !_application.Exited.IsCompleted)
        {
            throw new InvalidOperationException("The watcher must remain owned until actual application completion.");
        }

        // The application has actually ended. Only its SDK/watch helpers are in this nested scope.
        _watchJob.Terminate();
        while (_watchJob.ActiveProcessIds().Length != 0)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
        if (_watcher is not null)
        {
            await _watcher.Exited.ConfigureAwait(false);
            Emit("watcher-stopped", "watcher", _watcher.Process, code: "owned_tool_stop");
            _watcher.Process.Dispose();
            _watcher = null;
        }

        _watchStdout?.Dispose();
        _watchStderr?.Dispose();
        _watchStdout = null;
        _watchStderr = null;
        _watchJob.Dispose();
        _watchJob = null;
    }

    private async Task DrainApplicationExitAsync()
    {
        while (!_application!.Exited.IsCompleted || _applicationPeer?.Reader?.IsCompleted == false)
        {
            DrainRetirementReceipts();
            if (_force.Task.IsCompleted)
            {
                ForceOwnedProcesses("explicit_force");
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        await _application.Exited.ConfigureAwait(false);
        DrainRetirementReceipts();
    }

    private void DrainRetirementReceipts()
    {
        CheckInspectionDeadline();
        while (_messages.Reader.TryRead(out var incoming))
        {
            if (incoming.Role == "application" && incoming.Generation == _generation
                && incoming.Message is { Kind: "inspection-result" } result)
            {
                CompleteInspection(result, retiring: true);
            }
            if (incoming.Role == "application" && incoming.Generation == _generation
                && incoming.Message is { Kind: "app-activity" } message)
            {
                Emit("backend-activity", "application", _application!.Process, detail: message.Detail);
            }
            // Retiring UI/update frames cannot restore readiness or refresh data for another generation.
        }
    }

    private void DisposeWatchResources()
    {
        _backendInputs?.Dispose();
        _watchStdout?.Dispose();
        _watchStderr?.Dispose();
        _watcher?.Process.Dispose();
        _watchJob?.Dispose();
    }

    private void ReadContract()
    {
        var generated = File.ReadAllText(settings.GeneratedClient);
        var match = Regex.Match(generated, "const fingerprint = '([0-9a-f]{64})';", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            throw new SessionFailure("invalid_contract_output", "Build matching generated client bindings first.");
        }

        _contract = match.Groups[1].Value;
    }
}
