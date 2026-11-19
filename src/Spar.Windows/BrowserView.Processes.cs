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
    /// Ends the environment's renderer processes, the only way a hung page recovers (pitfall "A hung page"). WebView2
    /// has no API for it, so the process ids come from <c>GetProcessInfos</c>, which works only on the UI thread. The
    /// environment is the app's alone (one data folder per app, single instance), so its renderers are this window's.
    /// Returns how many were ended.
    /// </summary>
    public int TerminateRenderers()
    {
        if (_environment is null)
        {
            return 0;
        }

        // A test process's environment serves other tests' windows too: only this view's renderers are ended.
        if (SparTestSession.IsSupported && _source is { IsShared: true })
        {
            _ = TerminateOwnRenderersAsync();
            return 1;
        }

        var ended = 0;
        foreach (var process in RendererProcessIds(_environment))
        {
            if (ProcessControl.Terminate(process))
            {
                ended++;
            }
            else
            {
                _log.Warn($"Renderer process {process} could not be ended.");
            }
        }

        _log.Step($"renderer processes ended: {ended}");
        return ended;
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
    /// <summary>Ends this view's renderers only: a test process's environment serves other tests' windows too.</summary>
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
    /// <summary>The ids of the environment's renderer processes, from <c>GetProcessInfos</c> (UI thread only).</summary>
    private static List<int> RendererProcessIds(ICoreWebView2Environment10 environment)
    {
        var ids = new List<int>();
        var processes = Com.Take<ICoreWebView2ProcessInfoCollection>(environment.GetProcessInfos());
        try
        {
            var count = processes.GetCount();
            for (uint index = 0; index < count; index++)
            {
                var process = Com.Take<ICoreWebView2ProcessInfo>(processes.GetValueAtIndex(index));
                try
                {
                    if (process.GetKind() == WebView2Values.RendererProcess)
                    {
                        ids.Add(process.GetProcessId());
                    }
                }
                finally
                {
                    Com.Release(process);
                }
            }
        }
        finally
        {
            Com.Release(processes);
        }

        return ids;
    }
}
