using System.Diagnostics;
using Spar.Hosting;
using Windows.Win32;

namespace Spar.Windows;

/// <summary>What a close request came to: whether the window closed, and the last question a guard asked.</summary>
/// <param name="closed">The teardown ran; false when a guard or the person kept the window.</param>
/// <param name="question">The last question a guard asked (the page's or the backend's), null when none asked.</param>
/// <param name="unanswered">The page did not answer, and "Wait / Close anyway" was asked.</param>
internal readonly struct CloseReport(bool closed, string? question, bool unanswered)
{
    public bool Closed { get; } = closed;

    public string? Question { get; } = question;

    public bool Unanswered { get; } = unanswered;
}

/// <summary>
/// The two-stage close of the main window (decision "Two-stage close"; <c>docs/app-model.md</c> 6.11): the page's
/// guards, then the backend's, each closing, keeping or asking, one native confirmation per ask, and the teardown only
/// for an approved request. A second request joins the open one. When Windows ends the session (<c>WM_ENDSESSION</c>
/// with <c>TRUE</c>) the close is a non-cancellable, best-effort phase within 4 s of the message's start (decision
/// "Window tests: closing, session-end budget, test theme and any-character keys"): the page's guard gets at most 2 s
/// and shares 3 s with the backend's, nothing asks or keeps, then the teardown and the owner's last work (an app
/// disposes its services) until 4 s; then the window procedure returns, whatever is left, since Windows may end the
/// process once it has.
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>The page's guard at session end: at most 2 s, so the backend's always has at least one of the 3 s.</summary>
    private const int SessionEndPageMilliseconds = 2_000;

    /// <summary>The guards at session end, page and backend together.</summary>
    private const int SessionEndGuardsMilliseconds = CloseGuard.SessionEndDeadlineMilliseconds;

    /// <summary>How long <c>WM_ENDSESSION</c> is held at most, from its start: guards, teardown and the services' disposal.</summary>
    private const int SessionEndMilliseconds = 4_000;

    /// <summary>The open close request, which a second one joins.</summary>
    private Task<CloseReport>? _closeRequest;

    /// <summary>
    /// A close request (UI thread): <see cref="CloseReason.User"/> for the close button and Alt+F4, or a window test's
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

    /// <summary><c>WM_CLOSE</c>: the close button, Alt+F4, the taskbar; every close of the window is a close request.</summary>
    private void OnCloseMessage()
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

    /// <summary>The question a page's answer asks: its own, or the failed guard's; null for close and keep.</summary>
    private static string? PageQuestion(PageCloseAnswer answer) => answer.Kind switch
    {
        PageCloseKind.Ask => answer.Question,
        PageCloseKind.Failed => PageCloseAnswer.FailedQuestion,
        _ => null,
    };

    /// <summary>"Close anyway" or "Keep open": the native confirmation owned by the window, or a window test's answer.</summary>
    private Task<bool> ConfirmAsync(string question, ICloseQuestions? questions) =>
        questions?.ConfirmAsync(question) ?? Task.FromResult(NativeDialog.ConfirmClose(_window, _title, question));

    /// <summary>
    /// Windows ends the session (<c>WM_ENDSESSION</c> with <c>TRUE</c>): the session-end close, while the message is
    /// held, pumping, until the window is gone and its owner's last work is done, or 4 s from the message's start.
    /// </summary>
    private void EndSession()
    {
        var start = Stopwatch.GetTimestamp();
        _log.Step("session ending");

        // Windows ends the browser's processes too: nothing is recovered from here on.
        _view?.EndRecovery();
        var limit = start + Stopwatch.Frequency * SessionEndMilliseconds / 1_000;
        if (_state != State.Closing)
        {
            _closeRequest = SessionEndAsync(start, _automated);
        }

        var quit = MessagePump.Until(() => _destroyed, limit);
        var finished = false;
        if (_destroyed && _owner.SessionEnded() is { } remaining)
        {
            quit = MessagePump.Until(() => remaining.IsCompleted, limit) ?? quit;
            finished = remaining.IsCompleted;
        }

        var held = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var outcome = !_destroyed ? "the window did not close in time" : finished ? "window closed, last work done" : "window closed, last work still running";
        _log.Step($"session end: {outcome}; WM_ENDSESSION held {held:0} ms");
        if (SparTestSession.IsSupported)
        {
            _source?.OnPhase?.Invoke($"session end held {held:0} ms");
        }

        // Windows may end the process once this returns: the log's queued lines go to the disk first, within the budget.
        _owner.SessionEndLogged(TimeSpan.FromMilliseconds(SessionEndMilliseconds - held));
        if (quit is { } code)
        {
            // The loop below this one ends when it sees the quit message the pump took.
            PInvoke.PostQuitMessage(code);
        }
    }

    /// <summary>
    /// The session-end close: the page's guard with <c>sessionEnd</c> (at most 2 s), the backend's in what is left of
    /// 3 s, neither able to keep the window or ask, then the teardown, cut short at 4 s from <paramref name="start"/>.
    /// </summary>
    private async Task<CloseReport> SessionEndAsync(long start, bool automated)
    {
        string? question = null;
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

        await TeardownAsync(0, start + Stopwatch.Frequency * SessionEndMilliseconds / 1_000);
        return new CloseReport(closed: true, question, false);
    }

    /// <summary>What is left of <paramref name="milliseconds"/> from <paramref name="start"/>, never negative.</summary>
    private static TimeSpan Remaining(long start, int milliseconds)
    {
        var left = TimeSpan.FromMilliseconds(milliseconds) - Stopwatch.GetElapsedTime(start);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>A wait of <paramref name="milliseconds"/>, cut to what is left before <paramref name="deadline"/> when there is one.</summary>
    private static TimeSpan Bounded(int milliseconds, long? deadline)
    {
        var wait = TimeSpan.FromMilliseconds(milliseconds);
        if (deadline is not { } end)
        {
            return wait;
        }

        var left = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), end);
        return left <= TimeSpan.Zero ? TimeSpan.Zero : left < wait ? left : wait;
    }
}
