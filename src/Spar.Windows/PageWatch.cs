using System.Diagnostics;
using Spar.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>What the page's side of a close request decided (until the page's own close guard is built).</summary>
internal enum PageCloseAnswer
{
    /// <summary>The page answers, or there is no page to ask: the backend's guard is asked next.</summary>
    Agree,

    /// <summary>The page does not answer and the user chose "Close anyway": only the page's guard is skipped.</summary>
    CloseAnyway,
}

/// <summary>
/// The hung-page heartbeat of the main window (plan section 3, "Hung pages"), on the UI thread: a thread-pool timer
/// posts a tick every 5 s, each tick decides with <see cref="PageHeartbeat"/> and, at most, posts one small ping
/// through the bridge, so pinging costs the UI thread microseconds and never waits. The page answers from its
/// message handler. A page that stops answering gets Spar's native dialog, "Wait / Reload page", or "Wait / Close
/// anyway" while a close request is open (decision "What "Wait" does after the hung-page dialog"); the dialog runs on a
/// thread of its own and closes by itself when the page answers again. Each change reaches the app's hook
/// (<c>spar.OnPageUnresponsive</c>), which only observes. Nothing is ended or reloaded except by the user's answer; in
/// the automated mode nobody can answer, so no dialog shows, and a close closes anyway.
/// </summary>
internal sealed class PageWatch
{
    private const string MainWindow = "main";
    private const int WaitId = 200;
    private const int ReloadId = 201;
    private const int CloseAnywayId = 202;

    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly HWND _window;
    private readonly string _title;
    private readonly WebViewBridge _bridge;
    private readonly BrowserView _view;
    private readonly PageHangHook _hook;
    private readonly bool _automated;
    private readonly int _interval;
    private readonly PageHeartbeat _heartbeat = new();
    private readonly Action _tick;
    private ITimer? _timer;
    private PromptDialog? _dialog;
    private bool _dialogForClose;
    private TaskCompletionSource<PageCloseAnswer>? _closeWait;
    private bool _answeredSinceCloseRequest;
    private bool _stopped;
    private long _hangSince;
    private long _ticks;
    private long _pings;
    private long _longestTick;

    /// <param name="interval">The heartbeat's interval; <see cref="PageHeartbeat.IntervalMilliseconds"/> except in a Development override.</param>
    public PageWatch(
        UiThread ui,
        HostLog log,
        HWND window,
        string title,
        WebViewBridge bridge,
        BrowserView view,
        PageHangHook hook,
        bool automated,
        int interval)
    {
        _ui = ui;
        _log = log;
        _window = window;
        _title = title;
        _bridge = bridge;
        _view = view;
        _hook = hook;
        _automated = automated;
        _interval = interval;
        _tick = Tick;

        bridge.DocumentOpened += OnDocumentOpened;
        bridge.DocumentClosed += OnDocumentClosed;
        bridge.Answered += OnAnswered;
        view.RendererUnresponsive += OnRendererUnresponsive;
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
        _dialog?.Dismiss();
        _dialog = null;
        _closeWait?.TrySetResult(PageCloseAnswer.Agree);
        var longest = Stopwatch.GetElapsedTime(0, _longestTick).TotalMilliseconds;
        _log.Info($"Heartbeat: {_ticks} ticks, {_pings} pings, longest tick on the UI thread {longest:0.000} ms.");
    }

    /// <summary>
    /// The page's side of a close request (UI thread). A page already unresponsive gets "Wait / Close anyway" at once;
    /// one that answers pings must answer one within an interval (about 5 s), or it gets the same dialog. Wait keeps the
    /// request open on the dialog's schedule; a page that answers meanwhile lets the request go on. The page's own close
    /// guard (<c>windows.onCloseRequested</c>) is not built yet: when it is, its answer takes the ping's place.
    /// </summary>
    public async Task<PageCloseAnswer> AskBeforeCloseAsync()
    {
        if (_stopped || (!_heartbeat.IsArmed && !_heartbeat.IsUnresponsive))
        {
            return PageCloseAnswer.Agree;
        }

        _closeWait = new TaskCompletionSource<PageCloseAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answeredSinceCloseRequest = false;
        try
        {
            if (_heartbeat.IsUnresponsive)
            {
                AskWhileClosing();
            }
            else
            {
                SendPing(_heartbeat.PingNow(Stopwatch.GetTimestamp()));
                if (!_heartbeat.IsArmed)
                {
                    // The document went away, or cannot be pinged: there is no page left to wait for.
                    return PageCloseAnswer.Agree;
                }

                var first = await Task.WhenAny(_closeWait.Task, Task.Delay(_interval));
                if (first != _closeWait.Task && !_answeredSinceCloseRequest && !_stopped && _heartbeat.IsArmed)
                {
                    _log.Warn($"The page did not answer the close request within {_interval} ms.");
                    Apply(_heartbeat.ReportedUnresponsive(Stopwatch.GetTimestamp()), "did not answer the close request");
                }
            }

            return await _closeWait.Task;
        }
        finally
        {
            _closeWait = null;
        }
    }

    /// <summary>The close request ended without closing (the backend's guard or the user kept the window): back to watching.</summary>
    public void CloseRequestKept()
    {
        if (_heartbeat.IsUnresponsive && _heartbeat.IsAsking && _dialog is null)
        {
            _heartbeat.Waited();
        }
    }

    /// <summary>The window is closing while its page does not answer, after "Close anyway" (UI thread).</summary>
    public void ReportClosedAnyway()
    {
        var duration = HangDuration();
        _log.Step($"window closed anyway; the page had not answered for {duration.TotalSeconds:0.0} s");
        _hook.Report(new PageHang(MainWindow, PageHangState.ClosedAnyway, duration));
    }

    /// <summary>On the timer's thread: the tick runs on the UI thread, behind what is queued there.</summary>
    private void PostTick()
    {
        if (!_stopped)
        {
            _ui.Post(_tick, "heartbeat");
        }
    }

    private void Tick()
    {
        if (_stopped)
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var visible = PInvoke.IsWindowVisible(_window) && !PInvoke.IsIconic(_window) && _view.IsShown;
        Apply(_heartbeat.Tick(visible, started), "did not answer three heartbeats");

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

        if (_bridge.Ping(number))
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
        if (_automated)
        {
            // Nobody can answer: a close closes anyway, as an ask does in the automated mode; otherwise keep watching.
            _log.Info("The automated mode shows no hung-page dialog.");
            if (_closeWait is not null)
            {
                _closeWait.TrySetResult(PageCloseAnswer.CloseAnyway);
            }
            else
            {
                _heartbeat.Waited();
            }

            return;
        }

        if (_closeWait is not null)
        {
            AskWhileClosing();
            return;
        }

        Show(forClose: false);
    }

    private void AskWhileClosing()
    {
        if (_automated)
        {
            _log.Info("The automated mode closes a page that does not answer without asking.");
            _closeWait?.TrySetResult(PageCloseAnswer.CloseAnyway);
            return;
        }

        Show(forClose: true);
    }

    private void Show(bool forClose)
    {
        if (_dialog is not null)
        {
            if (_dialogForClose == forClose)
            {
                return;
            }

            _dialog.Dismiss();
        }

        var seconds = Math.Max(1, (int)Math.Round(HangDuration().TotalSeconds));
        var instruction = $"{_title} is not responding";
        var content = forClose
            ? $"The page has not answered for {seconds} seconds. You can wait for it, or close the window anyway: what the page has not saved is lost."
            : $"The page has not answered for {seconds} seconds. You can wait for it, or reload it: what the page has not saved is lost, and the app's other data is kept.";
        var second = forClose ? new PromptButton(CloseAnywayId, "Close anyway") : new PromptButton(ReloadId, "Reload page");
        var dialog = PromptDialog.Show(_window, _title, instruction, content, new PromptButton(WaitId, "Wait"), second, takeForeground: false, ReportDialogFailure);
        _dialog = dialog;
        _dialogForClose = forClose;
        _log.Step(forClose ? "hung-page dialog shown (Wait / Close anyway)" : "hung-page dialog shown (Wait / Reload page)");
        _ = AwaitChoiceAsync(dialog);
    }

    /// <summary>The user's answer, back on the UI thread; a dialog taken down or replaced meanwhile is ignored.</summary>
    private async Task AwaitChoiceAsync(PromptDialog dialog)
    {
        var choice = await dialog.Result;
        if (dialog != _dialog || _stopped)
        {
            return;
        }

        _dialog = null;
        switch (choice)
        {
            case WaitId:
                _log.Step("hung-page dialog: Wait");
                _heartbeat.Waited();
                break;
            case ReloadId:
                Reload();
                break;
            case CloseAnywayId:
                _log.Step("hung-page dialog: Close anyway");
                _closeWait?.TrySetResult(PageCloseAnswer.CloseAnyway);
                break;
        }
    }

    private void Reload()
    {
        var duration = HangDuration();
        _log.Step($"hung-page dialog: Reload page, after {duration.TotalSeconds:0.0} s");
        _hook.Report(new PageHang(MainWindow, PageHangState.Reloaded, duration));
        _heartbeat.Disarm();
        _view.ReloadHungPage();
    }

    private void Recovered()
    {
        var duration = HangDuration();
        _log.Step($"the page answered again after {duration.TotalSeconds:0.0} s");
        _hook.Report(new PageHang(MainWindow, PageHangState.Recovered, duration));
        _dialog?.Dismiss();
        _dialog = null;
        _closeWait?.TrySetResult(PageCloseAnswer.Agree);
    }

    private void OnDocumentOpened(bool answersPing)
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

    /// <summary>The document is gone: a hung one was replaced (its renderer ended and the page reloaded, or it navigated).</summary>
    private void OnDocumentClosed()
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
            _dialog?.Dismiss();
            _dialog = null;
        }

        _heartbeat.Disarm();
        _closeWait?.TrySetResult(PageCloseAnswer.Agree);
    }

    private void OnAnswered(long seq)
    {
        if (_stopped)
        {
            return;
        }

        _answeredSinceCloseRequest = true;
        Apply(_heartbeat.Answered(seq), "");
        if (!_heartbeat.IsUnresponsive)
        {
            _closeWait?.TrySetResult(PageCloseAnswer.Agree);
        }
    }

    private void OnRendererUnresponsive()
    {
        if (!_stopped)
        {
            Apply(_heartbeat.ReportedUnresponsive(Stopwatch.GetTimestamp()), "was reported unresponsive by WebView2");
        }
    }

    private void ReportDialogFailure(Exception exception) => _log.Error("The hung-page dialog failed", exception);

    private TimeSpan HangDuration() => _hangSince == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_hangSince);
}
