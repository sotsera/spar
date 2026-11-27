using System.Diagnostics;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The two-stage close of the main window on Linux (decision "Two-stage close"; <c>docs/app-model.md</c> 6.11): the
/// page's guards (<c>close-request</c>, through the heartbeat's <see cref="PageWatch"/>), then the backend's, each
/// closing, keeping or asking, one native confirmation per ask (or a window test's answer), and the teardown only for an
/// approved request. A page that does not answer gets "Wait / Close anyway". A second request joins the open one. A
/// close with <see cref="CloseReason.SessionEnd"/> runs the session end's rules and budgets (the guards' 3 s, the page's
/// 2 s, 4 s in all); <c>SIGTERM</c> and <c>SIGINT</c> start one (<see cref="SessionEnd"/>, <see cref="EndSession"/>).
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>How long the page's close work may take (plan section 2c, "Flush before close").</summary>
    private const int PageCloseWorkMilliseconds = 1_000;

    /// <summary>How long teardown waits for cancelled commands to end before closing the view anyway.</summary>
    private const int CommandSettleMilliseconds = 3_000;

    /// <summary>How long the loop keeps running for the web processes to exit once the window is destroyed.</summary>
    private const int WebProcessExitMilliseconds = 3_000;

    /// <summary>How long the teardown waits for the answers of the dialogs it took down; GLib gives them on the next turn.</summary>
    private const int DialogCloseMilliseconds = 1_000;

    /// <summary>How often the web processes are looked for meanwhile; they exited in 3–24 ms in the spikes.</summary>
    private const int WebProcessPollMilliseconds = 5;

    /// <summary>At session end, the page's guard's share of the guards' budget, so the backend's always has 1 s.</summary>
    private const int SessionEndPageMilliseconds = 2_000;

    /// <summary>At session end, both guards together (decision "Window tests: closing, session-end budget…").</summary>
    private const int SessionEndGuardsMilliseconds = CloseGuard.SessionEndDeadlineMilliseconds;

    /// <summary>At session end, everything: the guards, the teardown and the services' disposal.</summary>
    internal const int SessionEndMilliseconds = 4_000;

    /// <summary>The open close request, which a second one joins.</summary>
    private Task<CloseReport>? _closeRequest;

    /// <summary>
    /// A close request (loop thread): <see cref="CloseReason.User"/> for the window manager's close, or a window test's
    /// request with its reason and mode. <paramref name="questions"/> answers what would otherwise be a native dialog.
    /// A request while one is open joins it; during the teardown there is nothing left to ask.
    /// </summary>
    public Task<CloseReport> RequestCloseAsync(CloseReason reason, bool automated, ICloseQuestions? questions)
    {
        if (_state == State.Closing)
        {
            return Task.FromResult(new CloseReport(closed: true, null, false));
        }

        if (_closeRequest is { IsCompleted: false } open)
        {
            _log.Step("close requested again: it joins the open request");
            return open;
        }

        var request = reason == CloseReason.SessionEnd
            ? SessionEndAsync(Stopwatch.GetTimestamp(), automated)
            : CloseRequestedAsync(reason, automated, questions);
        _closeRequest = request;
        return request;
    }

    /// <summary>
    /// The end of the OS session (<see cref="SessionEnd"/>, loop thread), which started at <paramref name="start"/>: the
    /// session-end close in its place of an open close request, which can no longer keep the window (as Windows'
    /// <c>WM_ENDSESSION</c> does); nothing when the teardown has started. Completes once the window is gone.
    /// </summary>
    public Task<CloseReport> EndSession(long start)
    {
        if (_state == State.Closing)
        {
            _log.Step("session ending: the window is already closing");
            return _closeRequest ?? Task.FromResult(new CloseReport(closed: true, null, false));
        }

        var request = SessionEndAsync(start, _automated);
        _closeRequest = request;
        return request;
    }

    /// <summary>
    /// Plan section 3, "Teardown": mark closing, let the page finish its close work, stop admitting calls and let the
    /// cancelled ones settle (bounded, the loop still running), release the view, destroy the window, then keep the
    /// loop running until the web processes have exited. A page that does not answer gets no close work, and its web
    /// process is ended before the window is destroyed (plan section 3, "Hung pages"). A <paramref name="deadline"/>
    /// (a stopwatch timestamp; 0 for none) cuts the close work and the settling short, as the session end does.
    /// </summary>
    public async Task TeardownAsync(int exitCode, long deadline = 0)
    {
        if (_state == State.Closing)
        {
            return;
        }

        _state = State.Closing;
        _view?.EndRecovery();
        _owner.TeardownStarted();
        if (!_failed)
        {
            _exitCode = exitCode;
        }

        _log.Step("closing");
        var pageHung = _watch?.IsUnresponsive == true;
        _watch?.Stop();
        if (_bridge is { } bridge)
        {
            if (!pageHung)
            {
                await bridge.RunPageCloseWorkAsync(TimeSpan.FromMilliseconds(Within(deadline, PageCloseWorkMilliseconds)));
            }

            var settled = bridge.CloseAsync("window closing");
            _log.Step("calls stopped");
            var first = await Task.WhenAny(settled, Task.Delay(Within(deadline, CommandSettleMilliseconds)));
            _log.Step(first == settled ? "calls settled" : "calls still running at the limit");
        }

        _stopping.Cancel();
        if (pageHung)
        {
            _view?.TerminateHungWebProcess();
        }

        // A file dialog is cancelled and its answer awaited too: it holds the window as its parent.
        _fileDialogs?.CloseOpen();
        if (_fileDialogs is { } files && await Task.WhenAny(files.Closed, Task.Delay(DialogCloseMilliseconds)) != files.Closed)
        {
            _log.Warn($"The file dialog had not answered {DialogCloseMilliseconds} ms after it was cancelled.");
        }

        // Its dialogs go first, their answers read, so none is destroyed with the window (WindowDialogs).
        if (!await _dialogs.CloseAllAsync(DialogCloseMilliseconds))
        {
            _log.Warn($"A dialog of the window had not answered {DialogCloseMilliseconds} ms after it was taken down.");
        }

        _theme?.Close();
        _view?.Close();
        GLib.g_signal_handler_disconnect(_window, _closeHandler);
        Gtk.gtk_window_destroy(_window);
        _window = 0;
        _log.Step("window destroyed");
        _bridge?.Window?.Close();

        await WaitForWebProcessesAsync();
        _owner.WindowClosed();
    }

    /// <summary>The window's close as the window manager asks it (a test's "close as the user would", loop thread).</summary>
    public void CloseAsUser()
    {
        if (_window != 0 && _state != State.Closing)
        {
            Gtk.gtk_window_close(_window);
        }
    }

    /// <summary>The milliseconds of <paramref name="limit"/> left before <paramref name="deadline"/> (0: no deadline).</summary>
    private static int Within(long deadline, int limit)
    {
        if (deadline == 0)
        {
            return limit;
        }

        var left = (deadline - Stopwatch.GetTimestamp()) * 1_000 / Stopwatch.Frequency;
        return (int)Math.Clamp(left, 0, limit);
    }

    /// <summary>What is left of <paramref name="milliseconds"/> from <paramref name="start"/>, never negative.</summary>
    private static TimeSpan Remaining(long start, int milliseconds)
    {
        var left = TimeSpan.FromMilliseconds(milliseconds) - Stopwatch.GetElapsedTime(start);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>The window manager's close (the close button, a keyboard shortcut, a client message): a close request.</summary>
    private void OnCloseRequested()
    {
        if (_state != State.Closing)
        {
            _ = RequestCloseAsync(CloseReason.User, _automated, questions: null);
        }
    }

    /// <summary>
    /// The page's guards, then the backend's, each closing, keeping or asking; an ask shows one native confirmation (or
    /// takes a test's answer), and counts as close in the automated mode. Only an approved request starts the teardown.
    /// </summary>
    private async Task<CloseReport> CloseRequestedAsync(CloseReason reason, bool automated, ICloseQuestions? questions)
    {
        string? question = null;
        var unanswered = false;
        try
        {
            var request = new CloseRequest("main", reason, automated);
            _log.Step($"close requested ({reason}{(automated ? ", automated" : "")})");

            var page = _watch is null ? PageCloseResult.NoPage : await _watch.AskPageAsync(reason, automated, questions, TimeSpan.Zero);
            unanswered = page.Unanswered;
            if (_state == State.Closing)
            {
                return new CloseReport(closed: true, question, unanswered);
            }

            if (page.Answer.Kind == PageCloseKind.Keep)
            {
                _log.Step("close kept by the page's guard");
                return Kept();
            }

            if (PageQuestion(page.Answer) is { } asked)
            {
                question = asked;
                if (!automated && !await ConfirmAsync(asked, questions))
                {
                    _log.Step("close kept by the user (the page's question)");
                    return Kept();
                }
            }

            var verdict = _guard is null ? CloseVerdict.Close : await _guard.DecideAsync(request, _stopping.Token);
            question = verdict.Question ?? question;
            if (verdict.Kind == CloseVerdictKind.Keep)
            {
                _log.Step("close kept by the backend's guard");
                return Kept();
            }

            if (verdict.Kind == CloseVerdictKind.Ask && !await ConfirmAsync(verdict.Question!, questions))
            {
                _log.Step("close kept by the user (the backend's question)");
                return Kept();
            }

            if (page.ClosedAnyway)
            {
                _watch?.ReportClosedAnyway();
            }

            await TeardownAsync(0);
            return new CloseReport(closed: true, question, unanswered);
        }
        catch (Exception exception)
        {
            _log.Error("The close request failed", exception);
            return new CloseReport(_state == State.Closing, question, unanswered);
        }

        CloseReport Kept()
        {
            _watch?.CloseRequestKept();
            return new CloseReport(closed: false, question, unanswered);
        }
    }

    /// <summary>
    /// The session-end close: the page's guard with <c>sessionEnd</c> (at most 2 s), the backend's in what is left of
    /// 3 s, neither able to keep the window or ask, then the teardown, cut short at 4 s from <paramref name="start"/>.
    /// </summary>
    private async Task<CloseReport> SessionEndAsync(long start, bool automated)
    {
        string? question = null;
        _log.Step("session ending");
        _view?.EndRecovery();
        try
        {
            var request = new CloseRequest("main", CloseReason.SessionEnd, automated);
            var page = _watch is null
                ? PageCloseResult.NoPage
                : await _watch.AskPageAsync(CloseReason.SessionEnd, automated, questions: null, Remaining(start, SessionEndPageMilliseconds));
            question = PageQuestion(page.Answer);
            _log.Step($"session end: the page's guard answered {page.Answer.Kind}");

            var verdict = _guard is null
                ? CloseVerdict.Close
                : await _guard.DecideAsync(request, _stopping.Token, Remaining(start, SessionEndGuardsMilliseconds));
            question = verdict.Question ?? question;
            _log.Step($"session end: the backend's guard answered {verdict.Kind}; closing");
        }
        catch (Exception exception)
        {
            _log.Error("The session-end close failed", exception);
        }

        await TeardownAsync(0, start + (Stopwatch.Frequency * SessionEndMilliseconds / 1_000));
        return new CloseReport(closed: true, question, false);
    }

    /// <summary>The question a page's answer asks: its own, or the failed guard's; null for close and keep.</summary>
    private static string? PageQuestion(PageCloseAnswer answer) => answer.Kind switch
    {
        PageCloseKind.Ask => answer.Question,
        PageCloseKind.Failed => PageCloseAnswer.FailedQuestion,
        _ => null,
    };

    /// <summary>"Close anyway" or "Keep open": the native confirmation owned by the window, or a window test's answer.</summary>
    private async Task<bool> ConfirmAsync(string question, ICloseQuestions? questions)
    {
        if (questions is not null)
        {
            return await questions.ConfirmAsync(question);
        }

        if (_state == State.Closing || _window == 0)
        {
            return true;
        }

        var button = await AlertDialog.ChooseAsync(_window, _dialogs, question, "", "Close anyway", "Keep open");
        return button == 0;
    }
}
