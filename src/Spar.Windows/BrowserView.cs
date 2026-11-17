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
    private readonly Action<int, string> _fail;
    private readonly List<Subscription> _subscriptions = [];
    private ICoreWebView2Environment10? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2_22? _webView;
    private int _generation;
    private bool _closing;
    private bool _recovering;
    private bool _rendererReloadUsed;
    private bool _browserRecreated;
    private bool _watchingReadiness;

    public BrowserView(UiThread ui, HostLog log, HWND window, string profileFolder, PageServer pages, Task<WebViewBridge> bridge, Action<int, string> fail)
    {
        _ui = ui;
        _log = log;
        _window = window;
        _profileFolder = profileFolder;
        _pages = pages;
        _bridge = bridge;
        _fail = fail;
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
            var (code, message) = StartFailure(exception);
            _log.Error($"The WebView could not start: {message}", exception);
            _fail(code, message);
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
        _log.Info($"WebView2 Runtime {environment.GetBrowserVersionString()}");
        var exited = new WebView2EventHandler(args => OnBrowserExited(generation, args), Report);
        Subscribe(exited, environment.AddBrowserProcessExited(exited), environment.RemoveBrowserProcessExited);

        var controllerCreated = new ControllerCompleted();
        environment.CreateCoreWebView2Controller((nint)_window, controllerCreated);
        var controller = Com.Take<ICoreWebView2Controller>(await controllerCreated.Task);
        if (Abandoned(generation, controller))
        {
            return;
        }

        _controller = controller;
        _webView = Com.Take<ICoreWebView2_22>(controller.GetCoreWebView2());
        _log.Phase("controller");
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

    /// <summary>The start-up exit codes (plan section 3, "Missing prerequisites"); step 2 adds the full checks before this.</summary>
    private static (int Code, string Message) StartFailure(Exception exception) => exception switch
    {
        DllNotFoundException => (3, "The WebView2 loader is missing from the app."),
        WebView2Exception { Code: unchecked((int)0x80070002) } => (3, "The Microsoft Edge WebView2 Runtime is not installed."),
        WebView2Exception { Code: unchecked((int)0x8007139F) or unchecked((int)0x8000FFFF) } =>
            (5, "Another instance of the app with different settings is using the same profile folder."),
        InvalidCastException => (4, "The Microsoft Edge WebView2 Runtime is too old for this app."),
        _ => (5, "The Microsoft Edge WebView2 Runtime could not start: " + exception.Message),
    };

    private readonly record struct Subscription(WebView2EventHandler Handler, EventRegistrationToken Token, Action<EventRegistrationToken> Remove);
}
