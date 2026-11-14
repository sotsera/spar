using System.Globalization;

namespace Spar.PageHeartbeat;

/// <summary>
/// One run of the app under test, host-neutral: the heartbeat (the design under test) and a 1 s probe heartbeat that is
/// never gated and never alarms (its round trips feed the offline simulation of other intervals), the page's messages,
/// the timed actions, visibility gating, recovery timing and the backend state counter. Everything runs on the UI
/// thread; the hosts supply the bridge, navigation and window operations.
/// </summary>
internal abstract class Session
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TimedAction> _actions;
    private readonly string _arm;
    private readonly string _gate;
    private readonly string _onUnresponsive;
    private int _backendCounter;
    private bool _actionsStarted;
    private string _visibility = "?";
    private double _lastTickPerf = -1;
    private double _recoveryStart = -1;
    private string _recoveryKind = "";
    private bool _recoveryDoc;
    private bool _recoveryReady;
    private int _autoReloads;
    private PumpProbe? _pump;
    private Burner? _burner;
    private bool _stressOn;

    protected Session(Options options)
    {
        Options = options;
        _actions = TimedAction.Parse(options.Get("actions"));
        _arm = options.Get("arm", "doc");
        _gate = options.Get("gate", "window");
        _onUnresponsive = options.Get("on-unresponsive", "none");
        Beat = new Heartbeat("ping", options.Int("interval", 5000), options.Int("misses", 3), PostToPage);
        Probe = options.Int("probe-interval", 1000) is > 0 and var probeMs ? new Heartbeat("probe", probeMs, int.MaxValue, PostToPage) : null;
        Beat.Unresponsive += OnUnresponsive;
        Beat.Responsive += OnResponsive;
    }

    protected Options Options { get; }

    protected Heartbeat Beat { get; }

    protected Heartbeat? Probe { get; }

    protected PumpProbe? Pump => _pump;

    protected bool Finished => _stop.IsCancellationRequested;

    // ---- What a host supplies ------------------------------------------------------------------------------------

    /// <summary>Posts a string to the page's injected script; returns the HRESULT (0 on Linux).</summary>
    protected abstract int PostToPage(string text);

    protected abstract void NavigateTo(string path);

    protected abstract void ReloadPage();

    protected abstract void KillRenderer();

    /// <summary>Host-specific actions (window states, suspension, input); false when unknown.</summary>
    protected abstract bool HostAction(TimedAction action);

    /// <summary>Why the window counts as hidden right now ("" when visible).</summary>
    protected abstract string WindowHiddenReason();

    protected abstract void ShowUnresponsiveDialog();

    protected abstract void CloseUnresponsiveDialog(string why);

    protected abstract void Quit();

    // ---- Start ---------------------------------------------------------------------------------------------------

    /// <summary>Starts the heartbeats, gating poll and pump probe; call on the UI thread after the view exists.</summary>
    protected void StartSession(SynchronizationContext ui)
    {
        Log.Line("SESSION", $"arm={_arm} gate={_gate} intervalMs={Beat.IntervalMs} misses={Beat.MissLimit} probeMs={Probe?.IntervalMs ?? 0} onUnresponsive={_onUnresponsive} options=[{Options.Text}]");
        if (!Options.Has("no-ping"))
        {
            _ = Beat.RunAsync(_stop.Token);
        }

        if (Probe is not null)
        {
            Probe.Arm("start");
            _ = Probe.RunAsync(_stop.Token);
        }

        _ = GateLoopAsync();
        if (!Options.Has("no-pump"))
        {
            _pump = new PumpProbe(ui, 5000);
            _pump.Start();
        }

        if (Options.Int("end-after", 0) is > 0 and var endAfter)
        {
            // Runs whose page never becomes ready (a hang during load) end on this clock instead of --duration.
            _ = EndAfterAsync(endAfter);
        }

        var timeout = Options.Int("timeout", 300);
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(timeout));
            Log.Line("TIMEOUT", $"after={timeout}s");
            Environment.Exit(3);
        }) { IsBackground = true }.Start();
    }

    private async Task EndAfterAsync(int ms)
    {
        await Task.Delay(ms);
        Finish();
    }

    // ---- Navigation events from the host ------------------------------------------------------------------------

    /// <summary>starting, content-loading, completed-ok, completed-fail, process-exited.</summary>
    protected void OnNavigationEvent(string kind)
    {
        if (kind == _arm)
        {
            Beat.Arm(kind);
        }

        if (kind == "process-exited")
        {
            Beat.Disarm(kind);
        }
    }

    protected void OnRendererExited(string detail)
    {
        Log.Line("RENDERER-EXITED", detail);
        OnNavigationEvent("process-exited");
        if (Options.Get("on-exited", "reload") == "reload" && _autoReloads++ < 1)
        {
            // Plan: at most one automatic reload, only after a document had loaded; no backend replay.
            StartRecovery("auto-reload-after-exit");
            ReloadPage();
        }
    }

    // ---- Page messages ------------------------------------------------------------------------------------------

    protected void OnPageMessage(string text)
    {
        var parts = text.Split(':');
        switch (parts[0])
        {
            case "ping-pong":
                _visibility = parts[2];
                Beat.OnPong(long.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]);
                RecoveryPong("ping");
                break;
            case "probe-pong":
                _visibility = parts[2];
                Probe?.OnPong(long.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]);
                RecoveryPong("probe");
                break;
            case "doc":
                Log.Line("DOC", $"path={parts[1]} vis={parts[2]}");
                _visibility = parts[2];
                if (_recoveryStart >= 0 && !_recoveryDoc)
                {
                    _recoveryDoc = true;
                    Log.Line("RECOVERY-DOC", $"kind={_recoveryKind} ms={Log.Now - _recoveryStart:F0}");
                }

                OnNavigationEvent("doc");
                break;
            case "ready":
                Log.Line("READY", $"path={parts[1]} pagePerf={parts[2]} vis={parts[3]}");
                _visibility = parts[3];
                if (_recoveryStart >= 0 && !_recoveryReady)
                {
                    _recoveryReady = true;
                    Log.Line("RECOVERY-READY", $"kind={_recoveryKind} ms={Log.Now - _recoveryStart:F0}");
                }

                if (!_actionsStarted)
                {
                    _actionsStarted = true;
                    _ = RunActionsAsync();
                }

                break;
            case "tick":
                var perf = double.Parse(parts[2], CultureInfo.InvariantCulture);
                Log.Line("TICK", $"n={parts[1]} dtMs={(_lastTickPerf < 0 ? 0 : perf - _lastTickPerf):F0} vis={parts[3]} frames={parts[4]}");
                _lastTickPerf = perf;
                _visibility = parts[3];
                break;
            case "vis":
                _visibility = parts[1];
                Log.Line("VIS", $"state={parts[1]} pagePerf={parts[2]}");
                break;
            case "hang":
                Log.Line("HANG", $"kind={parts[1]} pagePerf={parts[2]}");
                break;
            case "state?":
                _backendCounter++;
                PostToPage($"state:{_backendCounter}");
                break;
            default:
                Log.Line("PAGE", text.Length > 200 ? text[..200] : text);
                break;
        }
    }

    private void RecoveryPong(string source)
    {
        // The first heartbeat answer from the new document (the 1 s probe usually comes first).
        if (_recoveryStart >= 0 && _recoveryDoc)
        {
            Log.Line("RECOVERY-PONG", $"kind={_recoveryKind} ms={Log.Now - _recoveryStart:F0} source={source}");
            _recoveryStart = -1;
        }
    }

    // ---- Unresponsive hook --------------------------------------------------------------------------------------

    private void OnUnresponsive(int missed)
    {
        Log.Line("HOOK-UNRESPONSIVE", $"missed={missed} action={_onUnresponsive}");
        switch (_onUnresponsive)
        {
            case "reload":
                StartRecovery("reload-on-unresponsive");
                ReloadPage();
                break;
            case "kill":
                StartRecovery("kill-on-unresponsive");
                KillRenderer();
                break;
            case "dialog":
                ShowUnresponsiveDialog();
                break;
        }
    }

    private void OnResponsive(double lasted)
    {
        if (_onUnresponsive == "dialog")
        {
            CloseUnresponsiveDialog("responsive-again");
        }
    }

    /// <summary>The dialog's "Reload page" answer.</summary>
    protected void ReloadFromDialog()
    {
        // WebView2's Reload does not replace a hung renderer, so the default answer terminates it first; the
        // exit handler then reloads once.
        if (Options.Get("dialog-reload", "kill") == "kill")
        {
            StartRecovery("dialog-kill");
            KillRenderer();
        }
        else
        {
            StartRecovery("dialog-reload");
            ReloadPage();
        }
    }

    // ---- Actions -------------------------------------------------------------------------------------------------

    private async Task RunActionsAsync()
    {
        var started = Log.Now;
        foreach (var action in _actions)
        {
            var wait = action.At - (Log.Now - started);
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(wait));
            }

            if (Finished)
            {
                return;
            }

            Log.Line("ACTION", $"verb={action.Verb} arg={action.Arg}");
            if (_pump is not null)
            {
                _pump.Phase = action.Arg.Length > 0 ? $"{action.Verb}-{action.Arg}" : action.Verb;
            }

            Do(action);
        }

        var rest = Options.Int("duration", 30000) - (Log.Now - started);
        if (rest > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(rest));
        }

        Finish();
    }

    private void Do(TimedAction action)
    {
        switch (action.Verb)
        {
            case "hang-idle" or "hang-handler" or "hang-microtask" or "hang-worker" or "state":
                PostToPage($"cmd:{action.Verb}");
                break;
            case "busy" or "load":
                PostToPage($"cmd:{action.Verb}:{action.Arg}");
                break;
            case "nav":
                StartRecovery("nav-" + action.Arg.Trim('/'));
                NavigateTo(action.Arg);
                break;
            case "reload":
                StartRecovery("reload");
                ReloadPage();
                break;
            case "kill":
                StartRecovery("kill");
                KillRenderer();
                break;
            case "burn-on":
                _burner ??= new Burner(int.Parse(action.Arg, CultureInfo.InvariantCulture));
                break;
            case "burn-off":
                _burner?.Dispose();
                _burner = null;
                break;
            case "stress-on":
                // stress-on:<messages per second>/<bytes>: posts from the UI thread, as the bridge would.
                var spec = action.Arg.Split('/');
                _stressOn = true;
                _ = StressAsync(int.Parse(spec[0], CultureInfo.InvariantCulture), int.Parse(spec[1], CultureInfo.InvariantCulture));
                break;
            case "stress-off":
                _stressOn = false;
                break;
            case "quit":
                Finish();
                break;
            default:
                try
                {
                    if (!HostAction(action))
                    {
                        Log.Line("ACTION-UNKNOWN", $"verb={action.Verb}");
                    }
                }
                catch (Exception exception)
                {
                    Log.Line("ACTION-FAILED", $"verb={action.Verb} error={exception.Message}");
                }

                break;
        }
    }

    protected void StartRecovery(string kind)
    {
        _recoveryStart = Log.Now;
        _recoveryKind = kind;
        _recoveryDoc = false;
        _recoveryReady = false;
        Log.Line("RECOVERY-START", $"kind={kind}");
    }

    private async Task StressAsync(int perSecond, int bytes)
    {
        var payload = "blob:" + new string('x', Math.Max(0, bytes - 5));
        var sent = 0L;
        var maxUs = 0.0;
        var totalUs = 0.0;
        var batch = Math.Max(1, perSecond / 100);
        while (_stressOn && !Finished)
        {
            for (var i = 0; i < batch; i++)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                PostToPage(payload);
                var us = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds;
                maxUs = Math.Max(maxUs, us);
                totalUs += us;
                sent++;
            }

            if (sent % (batch * 100) == 0)
            {
                Log.Line("STRESS", $"sent={sent} meanPostUs={totalUs / sent:F1} maxPostUs={maxUs:F0} mb={sent * bytes / 1048576.0:F1}");
                OnStressSample();
            }

            await Task.Delay(10);
        }

        Log.Line("STRESS-END", $"sent={sent} meanPostUs={(sent > 0 ? totalUs / sent : 0):F1} maxPostUs={maxUs:F0}");
    }

    /// <summary>Called about once a second during stress; hosts log process memory.</summary>
    protected virtual void OnStressSample()
    {
    }

    private async Task GateLoopAsync()
    {
        while (!Finished)
        {
            var reason = _gate is "window" or "both" ? WindowHiddenReason() : "";
            if (reason.Length == 0 && _gate is "page" or "both" && _visibility == "hidden")
            {
                reason = "page-hidden";
            }

            Beat.SetPaused(reason);
            await Task.Delay(250);
        }
    }

    protected void Finish()
    {
        if (Finished)
        {
            return;
        }

        _stop.Cancel();
        _stressOn = false;
        _burner?.Dispose();
        _pump?.Stop();
        Log.Line("FINISH", $"backendCounter={_backendCounter}");
        Quit();
    }
}

/// <summary>CPU load from inside the app process: N threads spinning at normal priority.</summary>
internal sealed class Burner : IDisposable
{
    private volatile bool _stop;

    public Burner(int threads)
    {
        // 0: two spinning threads per logical CPU.
        threads = threads > 0 ? threads : 2 * Environment.ProcessorCount;
        for (var i = 0; i < threads; i++)
        {
            new Thread(() =>
            {
                var x = 0L;
                while (!_stop)
                {
                    x++;
                }
            }) { IsBackground = true, Name = "burner" }.Start();
        }

        Log.Line("BURN", $"threads={threads} cpus={Environment.ProcessorCount}");
    }

    public void Dispose()
    {
        _stop = true;
        Log.Line("BURN-OFF", "");
    }
}
