using System.Diagnostics;

namespace Spar.Hosting;

/// <summary>
/// The hung-page heartbeat of the main window (plan section 3, "Hung pages"), for every host, on the window's UI thread:
/// a thread-pool timer posts a tick every 5 s, each tick decides with <see cref="PageHeartbeat"/> and, at most, posts
/// one small ping through the host's bridge, so pinging costs the UI thread microseconds and never waits. The host's
/// script in the page answers it. A page that stops answering gets the host's native dialog, "Wait / Reload page", or
/// "Wait / Close anyway" while a close request is open (decision "What "Wait" does after the hung-page dialog"); the
/// dialog closes by itself when the page answers again. Each change reaches the app's hook
/// (<c>spar.OnPageUnresponsive</c>), which only observes. Nothing is ended or reloaded except by the user's answer; in
/// the automated mode nobody can answer, so no dialog shows, and a close closes anyway. The page's side of a close
/// request, its close guards, is in <c>PageWatch.Closing.cs</c>; what the host provides is <see cref="IPageWatchHost"/>.
/// </summary>
internal sealed partial class PageWatch
{
    private const string MainWindow = "main";

    private readonly IPageWatchHost _host;
    private readonly HostLog _log;
    private readonly string _title;
    private readonly PageHangHook _hook;
    private readonly bool _automated;
    private readonly int _interval;
    private readonly PageHeartbeat _heartbeat = new();
    private readonly Action _tick;
    private ITimer? _timer;
    private IHostPrompt? _dialog;
    private bool _dialogForClose;
    private bool _stopped;
    private long _hangSince;
    private long _ticks;
    private long _pings;
    private long _longestTick;

    /// <param name="interval">The heartbeat's interval; <see cref="PageHeartbeat.IntervalMilliseconds"/> except in a Development override.</param>
    public PageWatch(IPageWatchHost host, HostLog log, string title, PageHangHook hook, bool automated, int interval)
    {
        _host = host;
        _log = log;
        _title = title;
        _hook = hook;
        _automated = automated;
        _interval = interval;
        _tick = Tick;
    }

    /// <summary>Whether the page counts as unresponsive now (UI thread).</summary>
    public bool IsUnresponsive => _heartbeat.IsUnresponsive;

    public void Start()
    {
        var period = TimeSpan.FromMilliseconds(_interval);
        _timer = TimeProvider.System.CreateTimer(static watch => ((PageWatch)watch!).PostTick(), this, period, period);
        _log.Info($"Heartbeat every {_interval} ms.");
    }

    /// <summary>The teardown began: no more pings, dialogs or hook calls (UI thread).</summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _timer?.Dispose();
        DismissDialog();
        _close?.Done.TrySetResult(PageCloseResult.NoPage);
        var longest = Stopwatch.GetElapsedTime(0, _longestTick).TotalMilliseconds;
        _log.Info($"Heartbeat: {_ticks} ticks, {_pings} pings, longest tick on the UI thread {longest:0.000} ms.");
    }

    /// <summary>
    /// A new document is created, before any of its scripts runs (UI thread): <paramref name="answersPing"/> when the
    /// host's script in it answers the heartbeat; a document without it is not pinged.
    /// </summary>
    public void DocumentOpened(bool answersPing)
    {
        if (_stopped)
        {
            return;
        }

        if (answersPing)
        {
            _heartbeat.Arm();
            _log.Info("Heartbeat armed for the new document.");
        }
        else
        {
            _heartbeat.Disarm();
            _log.Info("The new document does not answer the heartbeat; it is not pinged.");
        }
    }

    /// <summary>
    /// The document is gone (UI thread): a hung one was replaced (its process ended and the page reloaded, or it
    /// navigated). A close request it was asked goes on without it: the page that was asked is gone, so its answer no
    /// longer counts.
    /// </summary>
    public void DocumentClosed()
    {
        if (_stopped)
        {
            return;
        }

        if (_heartbeat.IsUnresponsive)
        {
            var duration = HangDuration();
            _log.Step($"the unresponsive page was replaced after {duration.TotalSeconds:0.0} s");
            _hook.Report(new PageHang(MainWindow, PageHangState.Reloaded, duration));
            DismissDialog();
        }

        _heartbeat.Disarm();
        if (_close is { } wait && wait.Done.TrySetResult(PageCloseResult.NoPage))
        {
            _log.Step($"close request {wait.Seq}: the page that was asked went away; its answer no longer counts");
        }
    }

    /// <summary>
    /// The current document answered the ping <paramref name="seq"/> (UI thread); <paramref name="dialog"/> when it said
    /// it shows a modal dialog of its own.
    /// </summary>
    public void Answered(long seq, bool dialog)
    {
        if (_stopped)
        {
            return;
        }

        if (_close is { } wait)
        {
            wait.Ponged(dialog);
            if (!wait.Sent && !_heartbeat.IsUnresponsive)
            {
                // A page without close guards (no SDK, or an older one) that answers agrees, as before guards existed.
                wait.Done.TrySetResult(PageCloseResult.NoPage);
            }
        }

        Apply(_heartbeat.Answered(seq), "");
    }

    /// <summary>
    /// The web engine reported the page unresponsive (UI thread): one state and one dialog with the heartbeat's alarm.
    /// <paramref name="engine"/> names it for the log.
    /// </summary>
    public void EngineReportedUnresponsive(string engine)
    {
        if (!_stopped)
        {
            Apply(_heartbeat.ReportedUnresponsive(Stopwatch.GetTimestamp()), $"was reported unresponsive by {engine}");
        }
    }

    /// <summary>On the timer's thread: the tick runs on the UI thread, behind what is queued there.</summary>
    private void PostTick()
    {
        if (!_stopped)
        {
            _host.Post(_tick, "heartbeat");
        }
    }

    private void Tick()
    {
        if (_stopped)
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        Apply(_heartbeat.Tick(_host.IsPageVisible, started), "did not answer three heartbeats");

        _ticks++;
        _longestTick = Math.Max(_longestTick, Stopwatch.GetTimestamp() - started);
    }

    private void Apply(HeartbeatAction action, string why)
    {
        switch (action)
        {
            case HeartbeatAction.Ping:
                SendPing(_heartbeat.Outstanding);
                break;
            case HeartbeatAction.Unresponsive:
                BecameUnresponsive(why);
                break;
            case HeartbeatAction.AskAgain:
                _log.Warn($"The page still does not answer after {HangDuration().TotalSeconds:0} s; asking again.");
                Ask();
                break;
            case HeartbeatAction.Recovered:
                Recovered();
                break;
        }
    }

    private void SendPing(long? seq)
    {
        if (seq is not { } number)
        {
            return;
        }

        if (_host.Ping(number))
        {
            _pings++;
            return;
        }

        // The document went away, or never answered pings after all: nothing is counted for it.
        _heartbeat.Disarm();
    }

    private void BecameUnresponsive(string why)
    {
        // Kept here: the heartbeat forgets it as soon as the hang ends, before its end is reported.
        _hangSince = _heartbeat.HangSince;
        var duration = HangDuration();
        _log.Warn($"The page is not responding: it {why} ({duration.TotalSeconds:0.0} s without an answer).");
        _hook.Report(new PageHang(MainWindow, PageHangState.Unresponsive, duration));
        Ask();
    }

    /// <summary>Shows the dialog for the current situation, replacing an open one of the other kind.</summary>
    private void Ask()
    {
        if (_close is not null)
        {
            AskWhileClosing(silentGuard: false);
            return;
        }

        if (_automated)
        {
            // Nobody can answer: keep watching.
            _log.Info("The automated mode shows no hung-page dialog.");
            _heartbeat.Waited();
            return;
        }

        Show(forClose: false, HangDuration());
    }

    private void Show(bool forClose, TimeSpan silentFor)
    {
        if (_dialog is not null)
        {
            if (_dialogForClose == forClose)
            {
                return;
            }

            _dialog.Dismiss();
        }

        var seconds = Math.Max(1, (int)Math.Round(silentFor.TotalSeconds));
        var instruction = $"{_title} is not responding";
        var content = forClose
            ? $"The page has not answered for {seconds} seconds. You can wait for it, or close the window anyway: what the page has not saved is lost."
            : $"The page has not answered for {seconds} seconds. You can wait for it, or reload it: what the page has not saved is lost, and the app's other data is kept.";
        var dialog = _host.ShowPrompt(new HostPromptRequest(forClose ? HostPromptKind.HungPageClose : HostPromptKind.HungPage, instruction, content, "Wait", forClose ? "Close anyway" : "Reload page"));
        _dialog = dialog;
        _dialogForClose = forClose;
        _log.Step(forClose ? "hung-page dialog shown (Wait / Close anyway)" : "hung-page dialog shown (Wait / Reload page)");
        _ = AwaitChoiceAsync(dialog, forClose);
    }

    /// <summary>The user's answer, back on the UI thread; a dialog taken down or replaced meanwhile is ignored.</summary>
    private async Task AwaitChoiceAsync(IHostPrompt dialog, bool forClose)
    {
        var choice = await dialog.Answer;
        if (dialog != _dialog || _stopped)
        {
            return;
        }

        _dialog = null;
        switch (choice)
        {
            case PromptAnswer.First:
                _log.Step("hung-page dialog: Wait");
                Waited();
                break;
            case PromptAnswer.Second when forClose:
                _log.Step("hung-page dialog: Close anyway");
                _close?.Done.TrySetResult(PageCloseResult.CloseAnyway);
                break;
            case PromptAnswer.Second:
                Reload();
                break;
        }
    }

    /// <summary>Wait: the heartbeat's schedule for a hung page, and the close request's for a silent guard.</summary>
    private void Waited()
    {
        _heartbeat.Waited();
        _close?.Waited(Stopwatch.GetTimestamp(), _interval);
    }

    private void Reload()
    {
        var duration = HangDuration();
        _log.Step($"hung-page dialog: Reload page, after {duration.TotalSeconds:0.0} s");
        _hook.Report(new PageHang(MainWindow, PageHangState.Reloaded, duration));
        _heartbeat.Disarm();
        _host.ReloadHungPage();
    }

    private void Recovered()
    {
        var duration = HangDuration();
        _log.Step($"the page answered again after {duration.TotalSeconds:0.0} s");
        _hook.Report(new PageHang(MainWindow, PageHangState.Recovered, duration));
        if (_close is not { SilentAsked: true })
        {
            DismissDialog();
        }

        if (_close is { Sent: false } wait)
        {
            // A page without close guards that answers again agrees; one with guards answers the request it now reads.
            wait.Done.TrySetResult(PageCloseResult.NoPage);
        }
    }

    private void DismissDialog()
    {
        _dialog?.Dismiss();
        _dialog = null;
    }

    private TimeSpan HangDuration() => _hangSince == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_hangSince);
}
