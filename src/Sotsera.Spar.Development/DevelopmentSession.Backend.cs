using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sotsera.Spar.Development;

public sealed partial class DevelopmentSession
{
    private WatcherProcess? _watcher;
    private string? _pendingDiagnostic;
    private long _pendingUpdate;
    private long _lastBackendRevision;
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
        if (_build.Process.ExitCode != 0 || version != WatcherProcess.SupportedSdk)
        {
            throw new SessionFailure("watch_sdk_not_verified", $"Backend watch mode requires the verified SDK {WatcherProcess.SupportedSdk}.");
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
            foreach (var line in output.ReadAvailable().Where(line => WatcherProcess.IsCompilerDiagnostic(line)).Take(16))
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
        _contractRestartPending = false;
        _watcher = new WatcherProcess(_job!);
        try
        {
            _watcher.Start(settings.Project, _directory, _generation, applicationVariables);
        }
        catch (Exception error) when (error is IOException or Win32Exception or ArgumentException)
        {
            throw new SessionFailure("watcher_launch_failed", error.Message);
        }
        finally
        {
            if (_watcher.Process is not null)
            {
                Emit("process-started", "watcher", _watcher.Process);
            }
        }
        _watcher.OpenOutput(_directory, _generation);
        return new Child(_watcher.Process!, "watcher", _watcher.Exited!);
    }

    private bool ReadWatchOutput()
    {
        if (_watcher is null)
        {
            return false;
        }

        var restart = false;
        foreach (var observation in _watcher.ReadAvailable())
        {
            switch (observation.Kind)
            {
                case "ready":
                    Emit("watcher-ready", "watcher", _watcher.Process);
                    break;
                case "diagnostic":
                    _pendingDiagnostic = observation.Detail;
                    Emit("watcher-diagnostic", code: "compiler_error", detail: _pendingDiagnostic);
                    break;
                case "restart":
                    restart = true;
                    Emit("restart-required", code: "unsupported_edit");
                    break;
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
        if (_watcher is null)
        {
            return;
        }
        await _watcher.RetireAsync(_application?.Exited).ConfigureAwait(false);
        if (_watcher.Process is not null)
        {
            Emit("watcher-stopped", "watcher", _watcher.Process, code: "owned_tool_stop");
        }
        _watcher.Dispose();
        _watcher = null;
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
