using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>The view's renderer processes: ending a hung page's renderer, and finding a test window's own.</summary>
internal sealed partial class BrowserView
{
    /// <summary>
    /// "Reload page" in the hung-page dialog (plan section 3, "Hung pages"): reloading alone never recovers a hung
    /// renderer, so the renderer is ended first, and the page loads again once it is gone. This is the user's choice, so
    /// it does not use up the automatic reload. A renderer that cannot be found is reloaded as it is (UI thread).
    /// </summary>
    public void ReloadHungPage()
    {
        _userReload = true;
        if (TerminateRenderers() == 0)
        {
            _userReload = false;
            _webView?.Reload();
            _log.Step("no renderer to end; page reloaded");
        }
    }

    /// <summary>
    /// Ends this window's renderer, the only way a hung page recovers (pitfall "A hung page"): the renderer that holds
    /// the view's main frame, from <c>GetProcessExtendedInfos</c>, never every renderer of the environment, since the
    /// app's other windows, and a test process's other tests, share its browser (decision "The window design, from the
    /// windows spike", item 1). Returns 0 when the view has no page yet; the renderers are ended asynchronously.
    /// </summary>
    public int TerminateRenderers()
    {
        if (_environment is null || _webView is null)
        {
            return 0;
        }

        _ = TerminateOwnRenderersAsync();
        return 1;
    }

    /// <summary>
    /// The renderer processes of this view's page, found by its main frame (UI thread). Window tests use it to end only
    /// their own page's renderer, and to check that it is gone after the window closed.
    /// </summary>
    public Task<IReadOnlyList<int>> RendererProcessesAsync()
    {
        if (_environment is null || _webView is null)
        {
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        var environment = (ICoreWebView2Environment13)(object)_environment;
        var completed = new FrameProcessesCompleted(_webView.GetFrameId());
        environment.GetProcessExtendedInfos(completed);
        return completed.Task;
    }
    /// <summary>Ends this view's renderers only; with none found, a user's "Reload page" reloads the page as it is.</summary>
    private async Task TerminateOwnRenderersAsync()
    {
        var ended = 0;
        foreach (var process in await RendererProcessesAsync())
        {
            ended += ProcessControl.Terminate(process) ? 1 : 0;
        }

        _log.Step($"renderer processes of this window ended: {ended}");
        if (ended == 0 && _userReload)
        {
            _userReload = false;
            _webView?.Reload();
        }
    }
}
