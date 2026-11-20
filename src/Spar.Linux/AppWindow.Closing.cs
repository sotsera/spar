using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The two-stage close of the main window on Linux (decision "Two-stage close"; <c>docs/app-model.md</c> 6.11): the
/// page's guards (<c>close-request</c>, through the heartbeat's <see cref="PageWatch"/>), then the backend's, each
/// closing, keeping or asking, one native confirmation per ask, and the teardown only for an approved request. A page
/// that does not answer gets "Wait / Close anyway". A second request joins the open one. The end of the session through
/// logind comes later.
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>How long the page's close work may take (plan section 2c, "Flush before close").</summary>
    private const int PageCloseWorkMilliseconds = 1_000;

    /// <summary>How long teardown waits for cancelled commands to end before closing the view anyway.</summary>
    private const int CommandSettleMilliseconds = 3_000;

    /// <summary>How long the loop keeps running for the web processes to exit once the window is destroyed.</summary>
    private const int WebProcessExitMilliseconds = 3_000;

    /// <summary>How often the web processes are looked for meanwhile; they exited in 3–24 ms in the spikes.</summary>
    private const int WebProcessPollMilliseconds = 5;

    /// <summary>The open close request, which a second one joins.</summary>
    private Task<bool>? _closeRequest;

    /// <summary>
    /// A close request (loop thread): <see cref="CloseReason.User"/> for the window manager's close, or a later step's
    /// request with its reason and mode. Completes with whether the window closed. A request while one is open joins it;
    /// during the teardown there is nothing left to ask.
    /// </summary>
    public Task<bool> RequestCloseAsync(CloseReason reason, bool automated)
    {
        if (_state == State.Closing)
        {
            return Task.FromResult(true);
        }

        if (_closeRequest is { IsCompleted: false } open)
        {
            _log.Step("close requested again: it joins the open request");
            return open;
        }

        var request = CloseRequestedAsync(reason, automated);
        _closeRequest = request;
        return request;
    }

    /// <summary>
    /// Plan section 3, "Teardown": mark closing, let the page finish its close work, stop admitting calls and let the
    /// cancelled ones settle (bounded, the loop still running), release the view, destroy the window, then keep the
    /// loop running until the web processes have exited. A page that does not answer gets no close work, and its web
    /// process is ended before the window is destroyed (plan section 3, "Hung pages").
    /// </summary>
    public async Task TeardownAsync(int exitCode)
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
                await bridge.RunPageCloseWorkAsync(TimeSpan.FromMilliseconds(PageCloseWorkMilliseconds));
            }

            var settled = bridge.CloseAsync("window closing");
            _log.Step("calls stopped");
            var first = await Task.WhenAny(settled, Task.Delay(CommandSettleMilliseconds));
            _log.Step(first == settled ? "calls settled" : "calls still running at the limit");
        }

        _stopping.Cancel();
        if (pageHung)
        {
            _view?.TerminateHungWebProcess();
        }

        _theme?.Close();
        _view?.Close();
        GLib.g_signal_handler_disconnect(_window, _closeHandler);
        Gtk.gtk_window_destroy(_window);
        _window = 0;
        _log.Step("window destroyed");

        await WaitForWebProcessesAsync();
        _owner.WindowClosed();
    }

    /// <summary>The window manager's close (the close button, a keyboard shortcut, a client message): a close request.</summary>
    private void OnCloseRequested()
    {
        if (_state != State.Closing)
        {
            _ = RequestCloseAsync(CloseReason.User, _automated);
        }
    }

    /// <summary>
    /// The page's guards, then the backend's, each closing, keeping or asking; an ask shows one native confirmation,
    /// and counts as close in the automated mode. Only an approved request starts the teardown.
    /// </summary>
    private async Task<bool> CloseRequestedAsync(CloseReason reason, bool automated)
    {
        try
        {
            var request = new CloseRequest("main", reason, automated);
            _log.Step($"close requested ({reason}{(automated ? ", automated" : "")})");

            var page = _watch is null ? PageCloseResult.NoPage : await _watch.AskPageAsync(reason, automated, questions: null, TimeSpan.Zero);
            if (_state == State.Closing)
            {
                return true;
            }

            if (page.Answer.Kind == PageCloseKind.Keep)
            {
                _log.Step("close kept by the page's guard");
                return Kept();
            }

            if (PageQuestion(page.Answer) is { } asked && !automated && !await ConfirmAsync(asked))
            {
                _log.Step("close kept by the user (the page's question)");
                return Kept();
            }

            var verdict = _guard is null ? CloseVerdict.Close : await _guard.DecideAsync(request, _stopping.Token);
            if (verdict.Kind == CloseVerdictKind.Keep)
            {
                _log.Step("close kept by the backend's guard");
                return Kept();
            }

            if (verdict.Kind == CloseVerdictKind.Ask && !await ConfirmAsync(verdict.Question!))
            {
                _log.Step("close kept by the user (the backend's question)");
                return Kept();
            }

            if (page.ClosedAnyway)
            {
                _watch?.ReportClosedAnyway();
            }

            await TeardownAsync(0);
            return true;
        }
        catch (Exception exception)
        {
            _log.Error("The close request failed", exception);
            return _state == State.Closing;
        }

        bool Kept()
        {
            _watch?.CloseRequestKept();
            return false;
        }
    }

    /// <summary>The question a page's answer asks: its own, or the failed guard's; null for close and keep.</summary>
    private static string? PageQuestion(PageCloseAnswer answer) => answer.Kind switch
    {
        PageCloseKind.Ask => answer.Question,
        PageCloseKind.Failed => PageCloseAnswer.FailedQuestion,
        _ => null,
    };

    /// <summary>"Close anyway" or "Keep open": the native confirmation owned by the window.</summary>
    private async Task<bool> ConfirmAsync(string question)
    {
        if (_state == State.Closing || _window == 0)
        {
            return true;
        }

        var button = await AlertDialog.ChooseAsync(_window, question, "", "Close anyway", "Keep open");
        return button == 0;
    }
}
