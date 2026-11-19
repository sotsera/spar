using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The two-stage close of the main window on Linux (decision "Two-stage close"; <c>docs/app-model.md</c> 6.11): the
/// backend's guard closes, keeps or asks, one native confirmation per ask, and the teardown runs only for an approved
/// request. A second request joins the open one. The page's own guards (<c>close-request</c>) come with the heartbeat
/// that carries them (M1c step 3), and the end of the session with logind (later); until then a request asks the
/// backend only.
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
    /// loop running until the web processes have exited.
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
        if (_bridge is { } bridge)
        {
            await bridge.RunPageCloseWorkAsync(TimeSpan.FromMilliseconds(PageCloseWorkMilliseconds));
            var settled = bridge.CloseAsync("window closing");
            _log.Step("calls stopped");
            var first = await Task.WhenAny(settled, Task.Delay(CommandSettleMilliseconds));
            _log.Step(first == settled ? "calls settled" : "calls still running at the limit");
        }

        _stopping.Cancel();
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
    /// The backend's guard closes, keeps or asks; an ask shows one native confirmation, and counts as close in the
    /// automated mode. Only an approved request starts the teardown.
    /// </summary>
    private async Task<bool> CloseRequestedAsync(CloseReason reason, bool automated)
    {
        try
        {
            var request = new CloseRequest("main", reason, automated);
            _log.Step($"close requested ({reason}{(automated ? ", automated" : "")})");

            var verdict = _guard is null ? CloseVerdict.Close : await _guard.DecideAsync(request, _stopping.Token);
            if (verdict.Kind == CloseVerdictKind.Keep)
            {
                _log.Step("close kept by the backend's guard");
                return false;
            }

            if (verdict.Kind == CloseVerdictKind.Ask && !await ConfirmAsync(verdict.Question!))
            {
                _log.Step("close kept by the user (the backend's question)");
                return false;
            }

            await TeardownAsync(0);
            return true;
        }
        catch (Exception exception)
        {
            _log.Error("The close request failed", exception);
            return _state == State.Closing;
        }
    }

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
