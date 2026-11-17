using System.Runtime.InteropServices;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>
/// The WebView in the app's window: its environment (on the app's profile folder), controller and events, all owned by
/// the UI thread. It recovers from failures (plan section 3, "Failure recovery"), handling <c>ProcessFailed</c> per view
/// and <c>BrowserProcessExited</c> per environment in either order: a renderer that exits gets a new document, with one
/// automatic reload and never a replayed command; a browser that exits gets the environment and controller created
/// again, once. Anything further is unrecoverable (exit code 5). Nothing recovers once shutdown has begun.
/// </summary>
internal sealed class BrowserView
{
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly HWND _window;
    private readonly string _profileFolder;
    private readonly PageServer _pages;
    private readonly Task<WebViewBridge> _bridge;
    private readonly WindowTheme _theme;
    private readonly bool _transparent;
    private readonly Action<int, string> _fail;
    private readonly Action<StartupProblem> _startupFailed;
    private readonly List<Subscription> _subscriptions = [];
    private ICoreWebView2Environment10? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2_22? _webView;
    private string? _runtimeVersion;
    private int _generation;
    private bool _closing;
    private bool _recovering;
    private bool _rendererReloadUsed;
    private bool _browserRecreated;
    private bool _watchingReadiness;

    /// <param name="transparent">
    /// Whether the WebView's default background is transparent over the window colour (plan section 3d). Always true
    /// in Production; a Development or Test build may turn it off to measure what it costs.
    /// </param>
    /// <param name="fail">An unrecoverable failure once running: the exit code and a message.</param>
    /// <param name="startupFailed">A start-up problem: the Runtime could not be started (exit 3 to 5).</param>
    public BrowserView(
        UiThread ui,
        HostLog log,
        HWND window,
        string profileFolder,
        PageServer pages,
        Task<WebViewBridge> bridge,
        WindowTheme theme,
        bool transparent,
        Action<int, string> fail,
        Action<StartupProblem> startupFailed)
    {
        _ui = ui;
        _log = log;
        _window = window;
        _profileFolder = profileFolder;
        _pages = pages;
        _bridge = bridge;
        _theme = theme;
        _transparent = transparent;
        _fail = fail;
        _startupFailed = startupFailed;
    }

    /// <summary>Creates the environment and the controller, then opens the page. Failures map to the start-up exit codes.</summary>
    public async Task StartAsync()
    {
        var generation = _generation;
        try
        {
            await CreateAsync(generation);
        }
        catch (Exception exception) when (!_closing)
        {
            var problem = StartupProblemOf(exception, _runtimeVersion);
            _log.Error($"The WebView could not start: {problem.Details}", exception);
            _startupFailed(problem);
        }
    }

    /// <summary>The page's colour scheme, on the profile: the app's choice, or Auto to follow the system (plan 3d).</summary>
    public void SetPageScheme(int scheme)
    {
        if (_webView is null)
        {
            return;
        }

        var profile = Com.Take<ICoreWebView2Profile>(_webView.GetProfile());
        try
        {
            profile.PutPreferredColorScheme(scheme);
        }
        finally
        {
            Com.Release(profile);
        }
    }

    /// <summary>Keeps the WebView the size of the window's client area (UI thread).</summary>
    public void Resize()
    {
        if (_controller is null || !PInvoke.GetClientRect(_window, out var client))
        {
            return;
        }

        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
    }

    /// <summary>
    /// Teardown step 5 (plan section 3, "Teardown"): no recovery from here on, the events removed, and the controller
    /// closed on the UI thread. The host then exits without waiting for the browser.
    /// </summary>
    public void Close()
    {
        _closing = true;
        Release(closeController: true);
    }

    private async Task CreateAsync(int generation)
    {
        Directory.CreateDirectory(_profileFolder);
        var created = new EnvironmentCompleted();
        var result = Native.CreateCoreWebView2EnvironmentWithOptions(null, _profileFolder, 0, created);
        if (result < 0)
        {
            throw new WebView2Exception("CreateCoreWebView2EnvironmentWithOptions", result);
        }

        var environment = Com.Take<ICoreWebView2Environment10>(await created.Task);
        if (Abandoned(generation, environment))
        {
            return;
        }

        _environment = environment;
        _log.Phase("environment");
        _runtimeVersion = environment.GetBrowserVersionString();
        _log.Info($"WebView2 Runtime {_runtimeVersion}");
        var exited = new WebView2EventHandler(args => OnBrowserExited(generation, args), Report);
        Subscribe(exited, environment.AddBrowserProcessExited(exited), environment.RemoveBrowserProcessExited);

        var controllerCreated = new ControllerCompleted();
        CreateController(environment, controllerCreated);
        var controller = Com.Take<ICoreWebView2Controller>(await controllerCreated.Task);
        if (Abandoned(generation, controller))
        {
            return;
        }

        _controller = controller;
        _webView = Com.Take<ICoreWebView2_22>(controller.GetCoreWebView2());
        _log.Phase("controller");
        SetPageScheme(_theme.PageScheme);
        Resize();

        var bridge = await _bridge;
        if (Abandoned(generation, null))
        {
            return;
        }

        SubscribeToWebView(generation, environment, _webView, bridge);
        PageServer.AddFilters(_webView);
        bridge.Attach(_webView);
        _webView.Navigate(PageServer.StartUrl);
        _log.Step("navigation started");
    }

    /// <summary>
    /// The controller with plan 3d's transparent default background, set on the creation options so the WebView never
    /// paints a colour of its own (white, or Chromium's dark <c>#121212</c>) before the page draws: the window colour
    /// shows through. <c>ICoreWebView2ControllerOptions3</c> needs Runtime 137, below Spar's minimum; it is still
    /// detected, so an older Runtime reached through a Development override only loses the transparency.
    /// </summary>
    private void CreateController(ICoreWebView2Environment10 environment, ControllerCompleted completed)
    {
        var options = environment.CreateCoreWebView2ControllerOptions();
        try
        {
            if (_transparent)
            {
                MakeTransparent(options);
            }

            environment.CreateCoreWebView2ControllerWithOptions((nint)_window, options, completed);
        }
        finally
        {
            Marshal.Release(options);
        }
    }

    private void MakeTransparent(nint options)
    {
        ICoreWebView2ControllerOptions3? withBackground = null;
        try
        {
            withBackground = Com.Borrow<ICoreWebView2ControllerOptions3>(options);
            withBackground.PutDefaultBackgroundColor(default);
        }
        catch (InvalidCastException)
        {
            _log.Warn("This WebView2 Runtime has no ICoreWebView2ControllerOptions3: the WebView keeps its default background.");
        }
        finally
        {
            Com.Release(withBackground);
        }
    }

    private void SubscribeToWebView(int generation, ICoreWebView2Environment10 environment, ICoreWebView2_22 webView, WebViewBridge bridge)
    {
        var requested = Handler(generation, args => _pages.OnRequested(environment, args));
        Subscribe(requested, webView.AddWebResourceRequested(requested), webView.RemoveWebResourceRequested);

        var starting = Handler(generation, _ => bridge.OnNavigationStarting());
        Subscribe(starting, webView.AddNavigationStarting(starting), webView.RemoveNavigationStarting);

        var loading = Handler(generation, _ =>
        {
            bridge.OnContentLoading();
            _log.Phase("first document");
        });
        Subscribe(loading, webView.AddContentLoading(loading), webView.RemoveContentLoading);

        var completed = Handler(generation, OnNavigationCompleted);
        Subscribe(completed, webView.AddNavigationCompleted(completed), webView.RemoveNavigationCompleted);

        var message = Handler(generation, bridge.OnMessage);
        Subscribe(message, webView.AddWebMessageReceived(message), webView.RemoveWebMessageReceived);

        var failed = Handler(generation, args => OnProcessFailed(generation, args, bridge));
        Subscribe(failed, webView.AddProcessFailed(failed), webView.RemoveProcessFailed);

        // A healthy document again: the renderer may be reloaded automatically once more. Subscribed once, for every generation.
        if (!_watchingReadiness)
        {
            _watchingReadiness = true;
            bridge.DocumentReady += () => _rendererReloadUsed = false;
        }
    }

    private void OnNavigationCompleted(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2NavigationCompletedEventArgs>(argsPointer);
        try
        {
            if (args.GetIsSuccess() != 0)
            {
                _log.Phase("first navigation completed");
                return;
            }

            _log.Warn($"Navigation {args.GetNavigationId()} failed with web error status {args.GetWebErrorStatus()}.");
        }
        finally
        {
            Com.Release(args);
        }
    }

    private void OnProcessFailed(int generation, nint argsPointer, WebViewBridge bridge)
    {
        var args = Com.Borrow<ICoreWebView2ProcessFailedEventArgs>(argsPointer);
        int kind;
        try
        {
            kind = args.GetProcessFailedKind();
        }
        finally
        {
            Com.Release(args);
        }

        if (_closing)
        {
            _log.Info($"A browser process failed during shutdown (kind {kind}); nothing is recovered.");
            return;
        }

        switch (kind)
        {
            case WebView2Values.RenderProcessExited:
                OnRendererExited(bridge);
                break;
            case WebView2Values.BrowserProcessExited:
                // The environment's BrowserProcessExited may come first or after; whichever does recovers, once.
                _ui.Post(() => _ = RecoverBrowserAsync(generation, "the browser process failed"));
                break;
            case WebView2Values.RenderProcessUnresponsive:
                _log.Warn("The page's renderer is not responding.");
                break;
            default:
                _log.Info($"A browser helper process exited (kind {kind}); WebView2 restarts it.");
                break;
        }
    }

    /// <summary>The page is gone with its renderer: its document ends, and a new one loads once, with no command replayed.</summary>
    private void OnRendererExited(WebViewBridge bridge)
    {
        bridge.OnNavigationStarting();
        _log.Step("renderer exited");
        if (_rendererReloadUsed)
        {
            _fail(5, "The page stopped working again right after it was reloaded.");
            return;
        }

        _rendererReloadUsed = true;
        _webView?.Reload();
        _log.Step("renderer reloaded");
    }

    private void OnBrowserExited(int generation, nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2BrowserProcessExitedEventArgs>(argsPointer);
        int kind;
        uint process;
        try
        {
            kind = args.GetBrowserProcessExitKind();
            process = args.GetBrowserProcessId();
        }
        finally
        {
            Com.Release(args);
        }

        if (_closing)
        {
            _log.Step($"browser process {process} exited");
            return;
        }

        _ui.Post(() => _ = RecoverBrowserAsync(generation, kind == WebView2Values.BrowserExitFailed
            ? $"browser process {process} failed"
            : $"browser process {process} exited"));
    }

    /// <summary>The environment and the controller are created again, once; a second loss of the browser ends the app.</summary>
    private async Task RecoverBrowserAsync(int generation, string reason)
    {
        if (_closing || _recovering || generation != _generation)
        {
            return;
        }

        _log.Step($"browser lost ({reason})");
        if (_browserRecreated)
        {
            _fail(5, "The web browser component stopped working again after it was restarted.");
            return;
        }

        _browserRecreated = true;
        _recovering = true;
        try
        {
            (await _bridge).Detach("browser lost");
            Release(closeController: true);
            _generation++;
            await CreateAsync(_generation);
            _log.Step("browser recreated");
        }
        catch (Exception exception) when (!_closing)
        {
            _log.Error("The web browser component could not be restarted", exception);
            _fail(5, "The web browser component stopped working and could not be restarted.");
        }
        finally
        {
            _recovering = false;
        }
    }

    /// <summary>A result that arrives after shutdown began, or for an older generation, is released and ignored.</summary>
    private bool Abandoned(int generation, object? created)
    {
        if (!_closing && generation == _generation)
        {
            return false;
        }

        if (created is ICoreWebView2Controller controller)
        {
            TryClose(controller);
        }

        Com.Release(created);
        return true;
    }

    private WebView2EventHandler Handler(int generation, Action<nint> handle) =>
        new(args =>
        {
            if (generation == _generation)
            {
                handle(args);
            }
        }, Report);

    private void Subscribe(WebView2EventHandler handler, EventRegistrationToken token, Action<EventRegistrationToken> remove) =>
        _subscriptions.Add(new Subscription(handler, token, remove));

    /// <summary>Removes the events, closes the controller and releases every WebView2 object, each step on its own.</summary>
    private void Release(bool closeController)
    {
        foreach (var subscription in _subscriptions)
        {
            try
            {
                subscription.Remove(subscription.Token);
            }
            catch (COMException)
            {
                // A WebView whose browser is gone refuses removals; its objects are released below anyway.
            }
        }

        _subscriptions.Clear();
        if (closeController && _controller is not null)
        {
            TryClose(_controller);
        }

        Com.Release(_webView);
        Com.Release(_controller);
        Com.Release(_environment);
        _webView = null;
        _controller = null;
        _environment = null;
    }

    private void TryClose(ICoreWebView2Controller controller)
    {
        try
        {
            controller.Close();
        }
        catch (COMException exception)
        {
            _log.Info($"The controller's Close returned 0x{exception.HResult:X8}.");
        }
    }

    private void Report(Exception exception) => _log.Error("A WebView2 event handler failed", exception);

    /// <summary>
    /// A start that failed after the checks of <see cref="Prerequisites"/> passed (plan section 3, "Missing
    /// prerequisites", step 5): environment creation succeeds even without a browser, so a broken Runtime shows here,
    /// mostly at controller creation. A Runtime that vanished since the check reads as missing; an interface the
    /// Runtime lacks, as too old.
    /// </summary>
    internal static StartupProblem StartupProblemOf(Exception exception, string? runtimeVersion) => exception switch
    {
        DllNotFoundException => new StartupProblem(
            StartupProblemKind.Missing, "WebView2 loader", null, null, "The app's file WebView2Loader.dll is missing. Install the app again.", null),
        WebView2Exception { Code: unchecked((int)0x80070002) } when runtimeVersion is null => Prerequisites.DecideRuntime(
            unchecked((int)0x80070002), null, Prerequisites.MinimumRuntime, comparisonFailed: false, comparison: 0)!,
        WebView2Exception { Code: unchecked((int)0x8007139F) or unchecked((int)0x8000FFFF) } and var different => new StartupProblem(
            StartupProblemKind.Broken,
            Prerequisites.Runtime,
            runtimeVersion,
            Prerequisites.MinimumRuntime,
            $"Another program uses this app's browser data folder with different settings (error 0x{different.Code:X8}). Close it, then start the app again.",
            null),
        InvalidCastException => new StartupProblem(
            StartupProblemKind.TooOld,
            Prerequisites.Runtime,
            runtimeVersion,
            Prerequisites.MinimumRuntime,
            "The Microsoft Edge WebView2 Runtime on this computer lacks a feature this app needs. Update it from Microsoft, then start the app again.",
            Prerequisites.RuntimeDownload),
        WebView2Exception failed => Prerequisites.ControllerFailed(runtimeVersion, failed.Code),
        _ => Prerequisites.ControllerFailed(runtimeVersion, exception.HResult),
    };

    private readonly record struct Subscription(WebView2EventHandler Handler, EventRegistrationToken Token, Action<EventRegistrationToken> Remove);
}
