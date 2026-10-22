using System.Diagnostics;
using System.IO.Pipes;

namespace Sotsera.Spar.Development;

public sealed partial class DevelopmentSession
{
    private TaskCompletionSource _debuggerEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DateTime _debuggerRequestedAt;
    private int _debuggerPaused;

    public void ReportDebuggerState(int generation, string state)
    {
        if (!settings.DebuggerBackend || generation != Volatile.Read(ref _generation))
        {
            return;
        }
        if (state == "paused")
        {
            Volatile.Write(ref _debuggerPaused, 1);
        }
        else if (state == "running")
        {
            Volatile.Write(ref _debuggerPaused, 0);
        }
        else if (state == "ended")
        {
            _debuggerEnded.TrySetResult();
        }
        else
        {
            return;
        }
        Emit("debugger-state", code: state);
    }

    private async Task<Peer> LaunchDebuggerAsync(NamedPipeServerStream pipe, Dictionary<string, string> variables)
    {
        _debuggerEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _debuggerPaused, 0);
        _debuggerRequestedAt = DateTime.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_startupStop.Token);
        var accept = AcceptAsync(pipe, "app", timeout.Token);
        try
        {
            variables["SPAR_DEV_BACKEND_OWNER"] = "debugger";
            Emit("backend-owner", code: "debugger");
            Emit("session-phase", code: "waiting-debugger");
            timeout.Token.ThrowIfCancellationRequested();
            await launchDebugger!(new DevelopmentDebugLaunch("debug-launch", _session, _generation,
                settings.Project, settings.Application, variables, ["--spar-dev"])).ConfigureAwait(false);
            var elapsed = TimeSpan.Zero;
            var previous = Stopwatch.GetTimestamp();
            while (!accept.IsCompleted)
            {
                await Task.WhenAny(accept, _debuggerEnded.Task, Task.Delay(100, timeout.Token)).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (_debuggerEnded.Task.IsCompleted)
                {
                    throw new SessionFailure("debugger_launch_failed", "The debugger ended before application admission.");
                }
                var now = Stopwatch.GetTimestamp();
                if (Volatile.Read(ref _debuggerPaused) == 0)
                {
                    elapsed += Stopwatch.GetElapsedTime(previous, now);
                }
                previous = now;
                if (elapsed > TimeSpan.FromSeconds(settings.StartupTimeoutSeconds))
                {
                    throw new SessionFailure("debugger_startup_timeout", "The debugger did not launch the authenticated application.");
                }
            }
            return await accept.ConfigureAwait(false);
        }
        catch
        {
            await timeout.CancelAsync().ConfigureAwait(false);
            try
            {
                var abandoned = await accept.ConfigureAwait(false);
                await abandoned.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception) { }
            throw;
        }
    }

    private void AdoptDebuggerProcess(Process process)
    {
        var expectedInstance = projectApplication is null
            ? process.StartTime.ToUniversalTime() >= _debuggerRequestedAt
            : process.Id == projectApplication.Id && process.StartTime == projectApplication.StartTime;
        if (!string.Equals(process.MainModule?.FileName, settings.Application, StringComparison.OrdinalIgnoreCase)
            || !expectedInstance)
        {
            throw new SessionFailure("debugger_process_mismatch", "The debugger launched a different executable or process instance.");
        }
        _job!.AdoptAuthenticatedDebuggerProcess(process);
        Emit("debugger-process-owned", "application", process);
    }
}
