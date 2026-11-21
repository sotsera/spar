using System.Diagnostics;

namespace Spar.Hosting;

/// <summary>What the page's side of a close request came to.</summary>
/// <param name="answer">The page guards' answer; <see cref="PageCloseAnswer.Close"/> when there is no page to ask.</param>
/// <param name="closedAnyway">The page did not answer and "Close anyway" was chosen: only the page's guard is skipped.</param>
/// <param name="unanswered">Spar's "Wait / Close anyway" was asked during the request, by a dialog or a test's answer.</param>
internal readonly struct PageCloseResult(PageCloseAnswer answer, bool closedAnyway = false, bool unanswered = false)
{
    public PageCloseAnswer Answer { get; } = answer;

    public bool ClosedAnyway { get; } = closedAnyway;

    public bool Unanswered { get; } = unanswered;

    /// <summary>No page could be asked, or the page that was asked went away: the page's side agrees.</summary>
    public static PageCloseResult NoPage { get; } = new(PageCloseAnswer.Close);

    public static PageCloseResult CloseAnyway { get; } = new(PageCloseAnswer.Close, closedAnyway: true, unanswered: true);
}

/// <summary>What a window's close request came to: whether the window closed, and the last question a guard asked.</summary>
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
/// How a close request's questions are answered: Spar's native dialogs by default, a window test's answers in its
/// place (decision "Window tests: closing, session-end budget, test theme and any-character keys"), so a window test
/// never shows a dialog.
/// </summary>
internal interface ICloseQuestions
{
    /// <summary>A guard's ask: true for "Close anyway", false for "Keep open".</summary>
    Task<bool> ConfirmAsync(string question);

    /// <summary>"Wait / Close anyway" for a page that does not answer the close request: true for "Close anyway".</summary>
    Task<bool> CloseUnansweredAsync();
}

/// <summary>
/// The page's side of a close request (decision "Two-stage close"; <c>docs/app-model.md</c> 6.11): the page's close
/// guards (<c>windows.onCloseRequested</c>) are asked with <c>close-request</c> and answer close, keep or ask. A ping
/// goes first, so the injected script's pong shows the page is alive, and whether it shows a dialog of its own, even
/// when a guard then blocks the page. A page that does not answer within an interval (about 5 s):
/// <list type="bullet">
/// <item>whose last pong said it shows a modal dialog of its own: is waited for, and checked again each interval;</item>
/// <item>that answered pings but not the request: its guard is silent, and "Wait / Close anyway" is asked;</item>
/// <item>that answered nothing: is hung, reported by the heartbeat, and asked the same.</item>
/// </list>
/// Wait keeps the request open and asks again on the hung-page schedule. A page without the SDK's guards (no
/// <c>close-request</c> in its <c>hello</c>) agrees when it answers the ping. At session end nothing is shown: the
/// guards get their budget, and the request ends with <c>close-ended</c> when it runs out.
/// </summary>
internal sealed partial class PageWatch
{
    private CloseWait? _close;
    private long _closeSeq;

    /// <summary>
    /// Asks the page (UI thread). <paramref name="questions"/> answers instead of the native dialog (a window test); in
    /// the <paramref name="automated"/> mode nobody can answer, so a silent or hung page is closed anyway. At session
    /// end the page gets <paramref name="sessionEndBudget"/> and no dialog.
    /// </summary>
    public async Task<PageCloseResult> AskPageAsync(CloseReason reason, bool automated, ICloseQuestions? questions, TimeSpan sessionEndBudget)
    {
        var sessionEnd = reason == CloseReason.SessionEnd;
        if (_stopped || _close is not null)
        {
            return PageCloseResult.NoPage;
        }

        if (sessionEnd && _heartbeat.IsUnresponsive)
        {
            _log.Step("session end: the page is not responding; its guard is skipped");
            return PageCloseResult.NoPage;
        }

        var wait = new CloseWait(++_closeSeq, automated, questions, Stopwatch.GetTimestamp());
        _close = wait;
        try
        {
            if (_heartbeat.IsUnresponsive)
            {
                AskWhileClosing(silentGuard: false);
                return await wait.Done.Task;
            }

            // The ping first: its pong is read before the request, so a guard that blocks the page cannot hide it.
            SendPing(_heartbeat.PingNow(Stopwatch.GetTimestamp()));
            wait.Sent = _host.RequestClose(wait.Seq, reason, automated);
            if (!wait.Sent && (!_heartbeat.IsArmed || sessionEnd))
            {
                // No page that answers anything, or session end with a page that has no guards: nothing to wait for.
                return PageCloseResult.NoPage;
            }

            _log.Step(wait.Sent ? $"close request {wait.Seq} sent to the page ({PageCloseAnswer.ReasonName(reason)})" : "the page has no close guards; pinging it");
            if (sessionEnd)
            {
                return await SessionEndAnswerAsync(wait, sessionEndBudget);
            }

            while (!wait.Done.Task.IsCompleted && !_stopped)
            {
                await Task.WhenAny(wait.Done.Task, Task.Delay(_interval));
                if (!wait.Done.Task.IsCompleted && !_stopped)
                {
                    CheckSilence(wait);
                }
            }

            return await wait.Done.Task;
        }
        finally
        {
            if (wait.SilentAsked || _dialogForClose)
            {
                DismissDialog();
            }

            if (wait.Sent && !wait.AnsweredByPage)
            {
                // The request ended without the page's answer: its guards' signal is aborted.
                _host.EndClose(wait.Seq);
            }

            _close = null;
        }
    }

    /// <summary>The close request ended without closing (a guard or the user kept the window): back to watching.</summary>
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
        if (!_heartbeat.IsUnresponsive)
        {
            // A page that answered pings but not its close request is not hung: the hook hears of hangs only.
            _log.Step("window closed anyway; the page's close guard had not answered");
            return;
        }

        var duration = HangDuration();
        _log.Step($"window closed anyway; the page had not answered for {duration.TotalSeconds:0.0} s");
        _hook.Report(new PageHang(MainWindow, PageHangState.ClosedAnyway, duration));
    }

    /// <summary>The page guards' answer (UI thread); one for a request that is no longer open is ignored.</summary>
    public void CloseAnswered(long seq, PageCloseAnswer answer, string? problem)
    {
        if (_close is not { } wait || wait.Seq != seq)
        {
            _log.Info($"A close answer for request {seq} came after its request ended; ignored.");
            return;
        }

        if (problem is not null)
        {
            _log.Warn($"Close request {seq}: the page's answer could not be read ({problem}).");
        }

        _log.Step($"close request {seq}: the page answered {answer.Kind}");
        wait.AnsweredByPage = true;
        wait.Done.TrySetResult(new PageCloseResult(answer, unanswered: wait.Unanswered));
    }

    /// <summary>At session end: the page's answer within its budget, or close without it.</summary>
    private async Task<PageCloseResult> SessionEndAnswerAsync(CloseWait wait, TimeSpan budget)
    {
        if (budget > TimeSpan.Zero)
        {
            await Task.WhenAny(wait.Done.Task, Task.Delay(budget));
        }

        if (wait.Done.Task.IsCompleted)
        {
            return await wait.Done.Task;
        }

        _log.Warn($"Session end: the page's close guard did not answer within {budget.TotalMilliseconds:0} ms; closing.");
        return PageCloseResult.NoPage;
    }

    /// <summary>An interval passed without the page's answer (UI thread): wait on, or ask "Wait / Close anyway".</summary>
    private void CheckSilence(CloseWait wait)
    {
        var now = Stopwatch.GetTimestamp();
        if (_heartbeat.IsUnresponsive || _dialog is not null || now < wait.AskAfter)
        {
            // The heartbeat's dialog or schedule has the request, a dialog is open, or the user chose to wait.
            wait.ResetPongs();
            SendPing(_heartbeat.PingNow(now));
            return;
        }

        if (!wait.PongedSinceCheck)
        {
            _log.Warn($"The page did not answer the close request within {_interval} ms.");
            Apply(_heartbeat.ReportedUnresponsive(now), "did not answer the close request");
            return;
        }

        if (wait.LastPongShowedDialog)
        {
            // The page shows a dialog of its own: its guard is waiting for the person.
            wait.ResetPongs();
            SendPing(_heartbeat.PingNow(now));
            return;
        }

        _log.Warn($"The page answers, but its close guard has not answered for {Stopwatch.GetElapsedTime(wait.Started).TotalSeconds:0} s.");
        wait.ResetPongs();
        SendPing(_heartbeat.PingNow(now));
        AskWhileClosing(silentGuard: true);
    }

    /// <summary>"Wait / Close anyway" for the open request: a test's answer, the automated mode's, or the native dialog.</summary>
    private void AskWhileClosing(bool silentGuard)
    {
        var wait = _close!;
        wait.Unanswered = true;
        if (wait.Automated)
        {
            _log.Info("The automated mode closes a page that does not answer without asking.");
            wait.Done.TrySetResult(PageCloseResult.CloseAnyway);
            return;
        }

        if (wait.Questions is { } questions)
        {
            if (!wait.Asking)
            {
                _ = AnswerUnansweredAsync(wait, questions);
            }

            return;
        }

        wait.SilentAsked = silentGuard || wait.SilentAsked;
        Show(forClose: true, silentGuard ? Stopwatch.GetElapsedTime(wait.Started) : HangDuration());
    }

    /// <summary>A window test answers "Wait / Close anyway" in place of the dialog.</summary>
    private async Task AnswerUnansweredAsync(CloseWait wait, ICloseQuestions questions)
    {
        wait.Asking = true;
        bool closeAnyway;
        try
        {
            closeAnyway = await questions.CloseUnansweredAsync();
        }
        finally
        {
            wait.Asking = false;
        }

        if (_close != wait || _stopped)
        {
            return;
        }

        _log.Step(closeAnyway ? "the test answered Close anyway" : "the test answered Wait");
        if (closeAnyway)
        {
            wait.Done.TrySetResult(PageCloseResult.CloseAnyway);
        }
        else
        {
            Waited();
        }
    }

    /// <summary>One close request's page side, on the UI thread.</summary>
    private sealed class CloseWait(long seq, bool automated, ICloseQuestions? questions, long started)
    {
        private int _waits;

        public long Seq { get; } = seq;

        public bool Automated { get; } = automated;

        public ICloseQuestions? Questions { get; } = questions;

        public long Started { get; } = started;

        public TaskCompletionSource<PageCloseResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The page listed <c>close-request</c> and was sent it.</summary>
        public bool Sent { get; set; }

        public bool AnsweredByPage { get; set; }

        /// <summary>"Wait / Close anyway" was asked for this request.</summary>
        public bool Unanswered { get; set; }

        /// <summary>The open dialog was asked for a silent guard rather than a hung page.</summary>
        public bool SilentAsked { get; set; }

        /// <summary>A test's answer is on its way.</summary>
        public bool Asking { get; set; }

        public bool PongedSinceCheck { get; private set; }

        public bool LastPongShowedDialog { get; private set; }

        /// <summary>Before this timestamp the user chose to wait, so nothing is asked.</summary>
        public long AskAfter { get; private set; }

        public void Ponged(bool dialog)
        {
            PongedSinceCheck = true;
            LastPongShowedDialog = dialog;
        }

        public void ResetPongs() => PongedSinceCheck = false;

        /// <summary>Wait, for a silent guard: asked again after 4, 8, 16, then every 32 intervals (20 s, 40 s, 80 s, 160 s at 5 s).</summary>
        public void Waited(long now, int interval)
        {
            AskAfter = now + Stopwatch.Frequency * interval / 1_000 * PageHeartbeat.AskAgainAfterTicks(_waits);
            _waits++;
        }
    }
}
