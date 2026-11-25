using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>
/// The WebView in the app's window: its environment (on the app's profile folder), controller and events, all owned by
/// the UI thread. It recovers from failures (plan section 3, "Failure recovery"), handling <c>ProcessFailed</c> per view
/// and <c>BrowserProcessExited</c> per environment in either order: a renderer that exits gets a new document, with one
/// automatic reload and never a replayed command; a browser that exits gets the environment and controller created
/// again, once. Anything further is unrecoverable (exit code 5). Nothing recovers once shutdown has begun. Before the
/// first navigation it adds the host's script to every document (<see cref="PageScript"/>), and its navigation,
/// new-window and permission events go to the <see cref="PageGuard"/> (plan section 3b).
/// </summary>
internal sealed partial class BrowserView
{
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly HWND _window;
    private readonly BrowserProfile _profile;
    private readonly BrowserDefaults _defaults;
    private readonly PageServer _pages;
    private readonly PageGuard _guard;
    private readonly string _pageScript;
    private readonly Task<WebViewBridge> _bridge;
    private readonly WindowTheme _theme;
    private readonly bool _transparent;
    private readonly Action<int, string> _fail;
    private readonly Action<StartupProblem> _startupFailed;
    private readonly BrowserSource? _source;
    private readonly List<Subscription> _subscriptions = [];
    private ICoreWebView2Environment10? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2_22? _webView;
    private string? _runtimeVersion;
    private int _generation;
    private bool _closing;
    private bool _ending;
    private bool _recovering;
    private bool _rendererReloadUsed;
    private bool _browserRecreated;
    private bool _watchingReadiness;
    private bool _userReload;

    /// <param name="transparent">
    /// Whether the WebView's default background is transparent over the window colour (plan section 3d). Always true
    /// in Production; a Development or Test build may turn it off to measure what it costs.
    /// </param>
    /// <param name="fail">An unrecoverable failure once running: the exit code and a message.</param>
    /// <param name="startupFailed">A start-up problem: the Runtime could not be started (exit 3 to 5).</param>
    /// <param name="source">Where the browser comes from: null for an app's own environment on its profile folder.</param>
    public BrowserView(
        UiThread ui,
        HostLog log,
        HWND window,
        BrowserProfile profile,
        BrowserDefaults defaults,
        PageServer pages,
        PageGuard guard,
        string pageScript,
        Task<WebViewBridge> bridge,
        WindowTheme theme,
        bool transparent,
        Action<int, string> fail,
        Action<StartupProblem> startupFailed,
        BrowserSource? source = null)
    {
        _ui = ui;
        _log = log;
        _window = window;
        _profile = profile;
        _defaults = defaults;
        _pages = pages;
        _guard = guard;
        _pageScript = pageScript;
        _bridge = bridge;
        _theme = theme;
        _transparent = transparent;
        _fail = fail;
        _startupFailed = startupFailed;
        _source = source;
        WindowState = new ViewWindowState(ui, log, window);
    }

    /// <summary>
    /// WebView2 reported the page's renderer unresponsive (<c>ProcessFailed</c>, <c>RenderProcessUnresponsive</c>): the
    /// same state as the heartbeat's alarm (plan section 3, "Hung pages"). Raised on the UI thread.
    /// </summary>
    public event Action? RendererUnresponsive;

    /// <summary>The window's focus, visibility and position, passed on to the controller (UI thread).</summary>
    public ViewWindowState WindowState { get; }

    /// <summary>The WebView, once its controller exists and until it closes (UI thread).</summary>
    public ICoreWebView2_22? WebView => _webView;

    /// <summary>
    /// The devtools' page automation of a Development build, installed before the first navigation as a test's page
    /// driver is; null in every other build, where the trimmer removes the call.
    /// </summary>
    public Func<Task>? DevtoolsInstall { get; set; }

    /// <summary>Whether the controller shows the WebView: a hidden controller's page is not pinged (UI thread).</summary>
    public bool IsShown => _controller is { } controller && controller.GetIsVisible() != 0;

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

    /// <summary>Keeps the WebView the size of the window's client area (UI thread); a minimized window keeps the last size.</summary>
    public void Resize()
    {
        if (_controller is null || PInvoke.IsIconic(_window) || !PInvoke.GetClientRect(_window, out var client))
        {
            return;
        }

        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
    }


    /// <summary>Runs a script in the page; for the Development-only triggers that make a page hang on demand.</summary>
    public Task<string?> RunScriptAsync(string script)
    {
        if (_webView is null)
        {
            return Task.FromResult<string?>(null);
        }

        var completed = new ScriptCompleted();
        _webView.ExecuteScript(script, completed);
        return completed.Task;
    }

    /// <summary>An in-process DevTools Protocol call on the WebView, for the Development-only input trigger (UI thread).</summary>
    public Task<string?> CallDevToolsAsync(string method, string parameters)
    {
        if (_webView is null)
        {
            return Task.FromResult<string?>(null);
        }

        var completed = new DevToolsCallCompleted();
        _webView.CallDevToolsProtocolMethod(method, parameters, completed);
        return completed.Task;
    }

    /// <summary>
    /// No recovery from here on (UI thread): an approved close or the session's end has started, so a browser or a renderer
    /// that is lost is not created or loaded again. At session end Windows ends the browser's processes with the session,
    /// and recreating it then only delayed the end (Windows Sandbox, 2026-11-19).
    /// </summary>
    public void EndRecovery() => _ending = true;

    /// <summary>
    /// Teardown step 5 (plan section 3, "Teardown"): no recovery from here on, the events removed, and the controller
    /// closed on the UI thread. The host then exits without waiting for the browser.
    /// </summary>
    public void Close()
    {
        _closing = true;
        Phase("closing");
        WindowState.LogSummary();
        Release(closeController: true);
    }

    private async Task CreateAsync(int generation)
    {
        // The feature switch is read in each condition, not kept in a local: the trimmer folds it there, so an app's
        // image has no shared-environment path at all.
        var environment = SparTestSession.IsSupported && _source is { IsShared: true }
            ? await SharedEnvironmentAsync()
            : await OwnEnvironmentAsync();
        if (Abandoned(generation, environment))
        {
            return;
        }

        _environment = environment;
        Phase("environment");
        _runtimeVersion = environment.GetBrowserVersionString();
        _log.Info($"WebView2 Runtime {_runtimeVersion}");
        if (!SparTestSession.IsSupported || _source is not { IsShared: true })
        {
            CheckProfileFolder(environment);
        }

        var exited = new WebView2EventHandler(
            args =>
            {
                using var scope = _ui.Enter("BrowserProcessExited");
                OnBrowserExited(generation, args);
            },
            Report);
        Subscribe(exited, environment.AddBrowserProcessExited(exited), environment.RemoveBrowserProcessExited);

        var controllerCreated = new ControllerCompleted();
        CreateController(environment, controllerCreated);
        Phase("controller requested");
        var controller = Com.Take<ICoreWebView2Controller>(await controllerCreated.Task);
        if (Abandoned(generation, controller))
        {
            return;
        }

        _controller = controller;
        _webView = Com.Take<ICoreWebView2_22>(controller.GetCoreWebView2());
        Phase("controller");
        WindowState.Attach(controller);
        _defaults.Apply(_webView);
        var picker = _defaults.BlocksPagePicker ? PagePicker.Block(_webView, _log) : Task.CompletedTask;
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

        // Development only: the page's console errors from the DevTools Protocol, so its links stay the page's own.
        var console = PageScript.ConsoleFromDevTools ? ReadConsole(_webView, bridge) : Task.CompletedTask;

        // Before the first navigation, so the script is in every document from the first one on, and the page's file
        // picker is intercepted from the first document on.
        var added = new ScriptAdded();
        _webView.AddScriptToExecuteOnDocumentCreated(_pageScript, added);
        await added.Task;
        await picker;
        await console;
        if (Abandoned(generation, null) || _webView is null)
        {
            return;
        }

        // A test's page driver installs itself here, after the host's own DevTools calls and before the first document.
        if (SparTestSession.IsSupported && _source?.BeforeFirstNavigation is { } beforeNavigation)
        {
            await beforeNavigation();
            if (Abandoned(generation, null) || _webView is null)
            {
                return;
            }
        }

        if (Spar.Devtools.SparDevtools.IsSupported && DevtoolsInstall is { } installDevtools)
        {
            try
            {
                await installDevtools();
            }
            catch (Exception exception)
            {
                // The page loads without its devtools; the app is not stopped for them.
                _log.Warn($"The devtools' page automation could not be installed: {exception.Message}");
            }

            if (Abandoned(generation, null) || _webView is null)
            {
                return;
            }
        }

        _log.Phase("page script added");
        _webView.Navigate(PageServer.StartUrl);
        Phase("navigation started");
    }

    /// <summary>An app's own environment, on its profile folder.</summary>
    private async Task<ICoreWebView2Environment10> OwnEnvironmentAsync()
    {
        Directory.CreateDirectory(_profile.Requested);
        var created = new EnvironmentCompleted();
        var options = new EnvironmentOptions(_defaults.BrowserArguments, Prerequisites.MinimumRuntime);
        var result = Native.CreateCoreWebView2EnvironmentWithOptions(null, _profile.Requested, options, created);
        if (result < 0)
        {
            throw new WebView2Exception("CreateCoreWebView2EnvironmentWithOptions", result);
        }

        return Com.Take<ICoreWebView2Environment10>(await created.Task);
    }

    /// <summary>The test process's environment for these browser arguments; the view's reference is its own.</summary>
    private async Task<ICoreWebView2Environment10> SharedEnvironmentAsync()
    {
        var pointer = await _source!.SharedEnvironment!(_defaults.BrowserArguments);
        return Com.Take<ICoreWebView2Environment10>(pointer);
    }

    /// <summary>A start-up phase: in the log once, and to the test host's hook when a test opened the window.</summary>
    private void Phase(string phase)
    {
        _log.Phase(phase);
        if (SparTestSession.IsSupported)
        {
            _source?.OnPhase?.Invoke(phase);
        }
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

            if (SparTestSession.IsSupported && _source?.PrivateProfile is { } profile)
            {
                MakePrivate(options, profile);
            }

            environment.CreateCoreWebView2ControllerWithOptions((nint)_window, options, completed);
        }
        finally
        {
            Marshal.Release(options);
        }
    }

    /// <summary>
    /// The profile folder the browser actually uses (plan section 3c): an administrator's policy, or an environment
    /// variable the app allowed, can move it. A moved folder is logged and recorded with the requested one in
    /// <see cref="BrowserProfile"/>, the folders no file grant may reach.
    /// </summary>
    private void CheckProfileFolder(ICoreWebView2Environment10 environment)
    {
        var inUse = environment.GetUserDataFolder();
        if (_profile.RecordInUse(inUse))
        {
            _log.Warn($"Browser profile folder moved by a policy or an allowed variable: {inUse}");
            return;
        }

        _log.Info($"Browser profile folder {inUse}");
    }

    /// <summary>
    /// A test's window: InPrivate, so nothing it stores outlives it, on a profile name no other window of the test
    /// process uses at the same time, since InPrivate windows of one name share their storage (decision "Step 8's
    /// shape: window tests driven by Spar's own API").
    /// </summary>
    private static void MakePrivate(nint options, string profile)
    {
        var settable = Com.Borrow<ICoreWebView2ControllerOptions3>(options);
        try
        {
            settable.PutIsInPrivateModeEnabled(1);
            settable.PutProfileName(profile);
        }
        finally
        {
            Com.Release(settable);
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
        var requested = Handler(generation, "WebResourceRequested", args => _pages.OnRequested(environment, args));
        Subscribe(requested, webView.AddWebResourceRequested(requested), webView.RemoveWebResourceRequested);

        // A navigation the lock lets through ends the current document's epoch; a cancelled one leaves it as it is.
        var starting = Handler(generation, "NavigationStarting", args =>
        {
            if (_guard.OnNavigationStarting(args))
            {
                bridge.OnNavigationStarting();
            }
        });
        Subscribe(starting, webView.AddNavigationStarting(starting), webView.RemoveNavigationStarting);

        var frameStarting = Handler(generation, "FrameNavigationStarting", _guard.OnFrameNavigationStarting);
        Subscribe(frameStarting, webView.AddFrameNavigationStarting(frameStarting), webView.RemoveFrameNavigationStarting);

        var newWindow = Handler(generation, "NewWindowRequested", _guard.OnNewWindowRequested);
        Subscribe(newWindow, webView.AddNewWindowRequested(newWindow), webView.RemoveNewWindowRequested);

        var permission = Handler(generation, "PermissionRequested", _guard.OnPermissionRequested);
        Subscribe(permission, webView.AddPermissionRequested(permission), webView.RemovePermissionRequested);

        var loading = Handler(generation, "ContentLoading", args =>
        {
            bridge.OnContentLoading(IsErrorPage(args));
            Phase("first document");
        });
        Subscribe(loading, webView.AddContentLoading(loading), webView.RemoveContentLoading);

        var completed = Handler(generation, "NavigationCompleted", OnNavigationCompleted);
        Subscribe(completed, webView.AddNavigationCompleted(completed), webView.RemoveNavigationCompleted);

        var message = Handler(generation, "WebMessageReceived", bridge.OnMessage);
        Subscribe(message, webView.AddWebMessageReceived(message), webView.RemoveWebMessageReceived);

        var failed = Handler(generation, "ProcessFailed", args => OnProcessFailed(generation, args, bridge));
        Subscribe(failed, webView.AddProcessFailed(failed), webView.RemoveProcessFailed);

        // Plan section 3c's defaults that are events rather than settings, each only where the app kept the default.
        if (_defaults.DeniesDownloads)
        {
            var download = Handler(generation, "DownloadStarting", _defaults.OnDownloadStarting);
            Subscribe(download, webView.AddDownloadStarting(download), webView.RemoveDownloadStarting);
        }

        if (_defaults.FiltersContextMenu)
        {
            var menu = Handler(generation, "ContextMenuRequested", _defaults.OnContextMenuRequested);
            Subscribe(menu, webView.AddContextMenuRequested(menu), webView.RemoveContextMenuRequested);
        }

        if (_controller is { } controller)
        {
            var tab = Handler(generation, "MoveFocusRequested", WindowState.OnMoveFocusRequested);
            Subscribe(tab, controller.AddMoveFocusRequested(tab), controller.RemoveMoveFocusRequested);
            if (_defaults.FiltersKeys)
            {
                var keys = Handler(generation, "AcceleratorKeyPressed", _defaults.OnAcceleratorKeyPressed);
                Subscribe(keys, controller.AddAcceleratorKeyPressed(keys), controller.RemoveAcceleratorKeyPressed);
            }
        }

        // A healthy document again: the renderer may be reloaded automatically once more. Subscribed once, for every generation.
        if (!_watchingReadiness)
        {
            _watchingReadiness = true;
            bridge.DocumentReady += () => _rendererReloadUsed = false;
        }
    }

    private static bool IsErrorPage(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2ContentLoadingEventArgs>(argsPointer);
        try
        {
            return args.GetIsErrorPage() != 0;
        }
        finally
        {
            Com.Release(args);
        }
    }

    private void OnNavigationCompleted(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2NavigationCompletedEventArgs>(argsPointer);
        try
        {
            if (args.GetIsSuccess() != 0)
            {
                Phase("first navigation completed");
                return;
            }

            _log.Warn($"Navigation {args.GetNavigationId()} failed with web error status {args.GetWebErrorStatus()}.");
            Phase("navigation failed");
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

        if (_closing || _ending)
        {
            _log.Info($"A browser process failed during shutdown (kind {kind}); nothing is recovered.");
            if (kind is WebView2Values.RenderProcessExited or WebView2Values.BrowserProcessExited)
            {
                // The page went with it: its document ends now, so a close request it was asked goes on without its
                // answer and its close work is not waited for. Before, a session end whose renderer died waited out
                // the page's 2 s and the close work's 1 s for a page that was gone (docs/evidence/renderer-sharing).
                bridge.OnRendererExited();
            }

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
                _log.Warn("WebView2 reports the page's renderer is not responding.");
                RendererUnresponsive?.Invoke();
                break;
            default:
                _log.Info($"A browser helper process exited (kind {kind}); WebView2 restarts it.");
                break;
        }
    }

    /// <summary>The page is gone with its renderer: its document ends, and a new one loads once, with no command replayed.</summary>
    private void OnRendererExited(WebViewBridge bridge)
    {
        bridge.OnRendererExited();
        _log.Step("renderer exited");
        if (_userReload)
        {
            // The user chose "Reload page" for a hung page: the renderer was ended for it, and the page loads once.
            _userReload = false;
            _webView?.Reload();
            _log.Step("page reloaded after the hung renderer was ended");
            return;
        }

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

        if (_closing || _ending)
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
        if (_closing || _ending || _recovering || generation != _generation)
        {
            return;
        }

        _log.Step($"browser lost ({reason})");

        // The test process's browser serves every test's window: the test that lost it fails rather than start another.
        if (SparTestSession.IsSupported && _source is { IsShared: true })
        {
            _fail(5, "The test process's web browser component stopped working.");
            return;
        }

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

    /// <summary>
    /// Development only (<see cref="PageScript.ConsoleFromDevTools"/>): the page's console errors from the DevTools
    /// Protocol's events, removed with the WebView's other events; completes once <c>Runtime.enable</c> did.
    /// </summary>
    private Task ReadConsole(ICoreWebView2_22 webView, WebViewBridge bridge)
    {
        var console = DevelopmentConsole.Start(webView, bridge, _log);
        var receiver = console.Receiver;
        Subscribe(console, receiver.AddDevToolsProtocolEventReceived(console), receiver.RemoveDevToolsProtocolEventReceived);
        return console.Enabled;
    }

    /// <summary>An event handler of this generation, named for the stall probe.</summary>
    private WebView2EventHandler Handler(int generation, string name, Action<nint> handle) =>
        new(args =>
        {
            using var scope = _ui.Enter(name);
            if (generation == _generation)
            {
                handle(args);
            }
        }, Report);


    private void Subscribe(object handler, EventRegistrationToken token, Action<EventRegistrationToken> remove) =>
        _subscriptions.Add(new Subscription(handler, token, remove));

    /// <summary>Removes the events, closes the controller and releases every WebView2 object, each step on its own.</summary>
    private void Release(bool closeController)
    {
        WindowState.Detach();
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

    private readonly record struct Subscription(object Handler, EventRegistrationToken Token, Action<EventRegistrationToken> Remove);
}
