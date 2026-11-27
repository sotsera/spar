using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Bridge;
using Spar.Hosting;
using Spar.Logging;
using Spar.Runtime;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>What the run that owns a window does at the window's turning points (UI thread).</summary>
internal interface IAppWindowOwner
{
    /// <summary>The teardown started: an app stops taking relaunches and starts watching its shutdown.</summary>
    void TeardownStarted();

    /// <summary>The window is gone: an app ends its message loop; a test window reports it is closed.</summary>
    void WindowDestroyed();

    /// <summary>
    /// Windows ends the session and the window's teardown has finished: what the run still does before the process may
    /// be ended (an app disposes its services), which the window waits for while it pumps. Null when there is nothing.
    /// </summary>
    Task? SessionEnded();

    /// <summary>
    /// The session end's last line is logged and <c>WM_ENDSESSION</c> is about to return, after which Windows may end the
    /// process at once: an app writes its log file out now, within <paramref name="limit"/> (what is left of the budget).
    /// </summary>
    void SessionEndLogged(TimeSpan limit);
}

/// <summary>
/// The app's main window: the window, shown at once in the theme's colour (plan 3d), the WebView, the bridge to the
/// app's services, the page's heartbeat (<see cref="PageWatch"/>), and the two-stage close (decision "Two-stage
/// close"): every close of the window is a close request, the page is checked and the backend's guard decides or asks,
/// and only an approved request starts the teardown; that part, and the end of the session, is in
/// <c>AppWindow.Closing.cs</c>. Everything here runs on the window's UI thread unless a member says otherwise.
/// </summary>
internal sealed partial class AppWindow : IWindowMessages, IReportsErrors
{
    /// <summary>A Development or Test build may set it to <c>default</c> to keep the WebView's own background (measurements).</summary>
    public const string WebViewBackgroundVariable = "SPAR_DEV_WEBVIEW_BACKGROUND";

    /// <summary>The exit code when the app could not start for a reason of its own, such as its services failing to build.</summary>
    public const int StartFailedExitCode = 70;

    /// <summary>How long the page's close work may take (plan section 2c, "Flush before close").</summary>
    private const int PageCloseWorkMilliseconds = 1_000;

    /// <summary>How long teardown waits for cancelled commands to end before closing the WebView anyway.</summary>
    private const int CommandSettleMilliseconds = 3_000;

    private readonly SparApp _app;
    private readonly HostLog _log;
    private readonly bool _automated;
    private readonly string _title;
    private readonly BrowserProfile _profile;
    private readonly PageHangHook _hangHook;
    private readonly IAppWindowOwner _owner;
    private readonly bool _offscreen;
    private readonly BrowserSource? _source;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<WebViewBridge> _bridge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CspViolationLog? _violations;
    private PageErrorLog? _pageErrors;
    private bool _listenerReports;
    private WindowTheme? _theme;
    private SparRunningApp? _running;
    private CloseGuard? _guard;
    private UiThread? _ui;
    private BrowserView? _view;
    private PageWatch? _watch;
    private HWND _window;
    private HBRUSH _background;
    private State _state;
    private bool _failed;
    private bool _destroyed;
    private bool _sessionEnding;
    private bool _tornDown;
    private int _exitCode;
    private Spar.DevServer.DevServerSession? _devServer;
    private Spar.Devtools.DevtoolsHost? _devtools;

    /// <param name="offscreen">
    /// A window of a test process (plan section 5): shown offscreen, with no taskbar button, never activated.
    /// </param>
    /// <param name="source">Where the WebView's browser comes from: null for the app's own environment.</param>
    public AppWindow(
        SparApp app,
        HostLog log,
        bool automated,
        string title,
        BrowserProfile profile,
        PageHangHook hangHook,
        IAppWindowOwner owner,
        bool offscreen = false,
        BrowserSource? source = null)
    {
        _app = app;
        _log = log;
        _automated = automated;
        _title = title;
        _profile = profile;
        _hangHook = hangHook;
        _owner = owner;
        _offscreen = offscreen;
        _source = source;
    }

    /// <summary>The window's handle, once <see cref="Start"/> created it.</summary>
    public HWND Handle => _window;

    /// <summary>The colour the window paints behind the transparent page now, as <c>0x00BBGGRR</c>; 0 before <see cref="Start"/>.</summary>
    public uint WindowColour => _theme?.Background.ColorRef ?? 0;

    /// <summary>The run's exit code: 0, or the code of the failure that ended it.</summary>
    public int ExitCode => _exitCode;

    /// <summary>The WebView, once <see cref="Start"/> created it.</summary>
    public BrowserView? View => _view;

    /// <summary>Whether the teardown has started.</summary>
    public bool IsClosing => _state == State.Closing;

    /// <summary>What a start-up report needs from this run.</summary>
    public StartupContext Startup => new(_title, _app.Builder.StartupProblemHandler, _log, _automated);

    public void Report(Exception exception) => _log.Error("The window's thread failed", exception);

    /// <summary>
    /// A Debug build's development server, which the page comes from (before <see cref="Start"/>). Called only behind
    /// <see cref="Spar.DevServer.DevServerSettings.IsSupported"/>, so no other build has it.
    /// </summary>
    public void UseDevServer(Spar.DevServer.DevServerSession devServer) => _devServer = devServer;

    /// <summary>The devtools of a Development build: its page automation goes into the view before the first navigation.</summary>
    public void UseDevtools(Spar.Devtools.DevtoolsHost devtools) => _devtools = devtools;

    /// <summary>
    /// The window first, in the theme's colour and title bar before it is shown (plan 3d), then everything that takes
    /// time, without blocking the thread: the WebView, and the bridge once the app's services exist.
    /// </summary>
    public void Start(UiThread ui, WindowTheme theme, Task<SparRunningApp> built)
    {
        _ui = ui;
        _theme = theme;
        _background = PInvoke.CreateSolidBrush(new COLORREF(theme.Background.ColorRef));
        var bounds = WindowPlacement.Initial(_app.Builder.MainWindow.InitialSize);
        if (SparTestSession.IsSupported && _offscreen)
        {
            // Offscreen and never activated: no taskbar button, no focus taken from the person at the desktop.
            bounds = WindowPlacement.Offscreen(bounds);
            _window = ui.CreateWindow(_title, WindowPlacement.Style, bounds, this, WindowPlacement.TestExtendedStyle);
            theme.ApplyTitleBar(_window);
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }
        else
        {
            var maximized = false;
            if (SparDevelopment.IsSupported && WindowPlacement.Kept() is { } kept)
            {
                // spar dev's restart: the previous run's place and size; its start shows it without the focus.
                bounds = kept.Bounds;
                maximized = kept.Maximized;
            }

            _window = ui.CreateWindow(_title, WindowPlacement.Style, bounds, this);
            theme.ApplyTitleBar(_window);
            if (SparDevelopment.IsSupported && maximized)
            {
                // Maximized again without activation (SW_SHOWMAXIMIZED would take the focus): hidden, then SW_SHOWNA.
                WindowPlacement.MaximizeHidden(_window, bounds);
                PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNA);
            }
            else
            {
                PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWDEFAULT);
            }
        }

        _log.Phase("window shown");
        _log.Info($"Theme {(theme.IsDark ? "dark" : "light")} ({(theme.AppChoosesDark is null ? "system" : "app's choice")}), window {theme.Background}.");

        var frontend = _app.Catalog.AppAssembly is { } assembly ? SparFrontend.Open(assembly) : null;
        var security = PageSecuritySetup.Resolve(_app, _log.Logger);
        var development = SparDevelopment.IsSupported;
        var endpointReports = PagePolicy.Reports(security.Endpoint, development);
        _listenerReports = PagePolicy.Reports(security.Listener, development);
        if (endpointReports || _listenerReports)
        {
            // The advice only in Development: behind the feature switch, so the trimmer removes it from Production.
            _violations = new CspViolationLog(_log.Logger, TimeProvider.System, SparDevelopment.IsSupported ? CspAdvice.For : null);
        }

        var pages = new PageServer(frontend, ui, _log, security, development, endpointReports ? _violations : null);
        if (Spar.DevServer.DevServerSettings.IsSupported && _devServer is { } devServer)
        {
            // A development server that stops takes the page to the error page, which offers Retry.
            pages.UseDevServer(devServer, security);
            devServer.Stopped += () => ui.Post(() =>
            {
                if (_state != State.Closing)
                {
                    _view?.WebView?.Navigate(PageServer.StartUrl);
                }
            });
        }

        // The optional telemetry layer: its routes on the page's origin, and its settings in the injected script, so the
        // page loads its telemetry code only when an endpoint is set (section 6b).
        var telemetry = Spar.Telemetry.TelemetryRoute.For(_app.Builder);
        pages.UseTelemetry(telemetry);
        pages.UseBlobs(built);

        _ = LogPolicyAsync(pages);
        var guard = new PageGuard(_log, security, _app.Builder.BrowserOptions.HistoryNavigation);
        var defaults = new BrowserDefaults(_log, _app.Builder.BrowserOptions, development, _automated);
        var script = PageScript.Text(_listenerReports);
        if (telemetry.PageScript() is { } settings)
        {
            script += "\n" + settings;
        }
        _view = new BrowserView(
            ui, _log, _window, _profile, defaults, pages, guard, script, _bridge.Task, theme, TransparentWebView(), Fail, StartupFailed, _source);
        if (Spar.Devtools.SparDevtools.IsSupported && _devtools is { } devtools
            && Spar.Devtools.DevtoolsHost.EngineScript(_app.Catalog.AppAssembly) is { } engine)
        {
            var view = _view;
            view.DevtoolsInstall = () => Devtools.DevtoolsPage.InstallAsync(ui, view, _log, devtools, engine);
        }

        _ = _view.StartAsync();
        _ = ConnectAsync(ui, built);
    }

    public LRESULT? OnMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        using var scope = MessageName(message) is { } name && _ui is { } ui ? ui.Enter(name) : default;
        switch (message)
        {
            case PInvoke.WM_ERASEBKGND:
                return PaintBackground(window, wParam);
            case PInvoke.WM_SIZE:
                _view?.Resize();
                return default(LRESULT);
            case PInvoke.WM_WINDOWPOSCHANGED:
                // Shown, hidden, minimized or moved; DefWindowProc then sends WM_SIZE and WM_MOVE.
                _view?.WindowState.OnPositionChanged(lParam);
                return null;
            case PInvoke.WM_SETFOCUS:
                _view?.WindowState.OnSetFocus();
                return default(LRESULT);
            case PInvoke.WM_GETMINMAXINFO:
                WindowPlacement.ApplyMinimum(window, _app.Builder.MainWindow.Minimum, lParam);
                return default(LRESULT);
            case PInvoke.WM_DPICHANGED:
                WindowPlacement.OnDpiChanged(window, lParam);
                return default(LRESULT);
            case PInvoke.WM_SETTINGCHANGE:
                OnSettingChange(window, lParam);
                return null;
            case PInvoke.WM_CLOSE:
                // Every close goes through a close request; the window is destroyed only by the teardown.
                OnCloseMessage();
                return default(LRESULT);
            case PInvoke.WM_QUERYENDSESSION:
                // Spar never blocks Windows from logging off (decision "Two-stage close").
                return new LRESULT(1);
            case PInvoke.WM_ENDSESSION:
                if (wParam.Value != 0)
                {
                    EndSession();
                }

                return default(LRESULT);
            case DestroyAfterSessionEnd:
                PInvoke.DestroyWindow(_window);
                return default(LRESULT);
            case PInvoke.WM_DESTROY:
                _destroyed = true;
                DeleteBackground();
                _owner.WindowDestroyed();
                return default(LRESULT);
            default:
                return null;
        }
    }

    /// <summary>Shows and restores the window and asks for the foreground, which the relaunching process allowed.</summary>
    public void BringForward(int arguments)
    {
        if (PInvoke.IsIconic(_window))
        {
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_RESTORE);
        }
        else if (!PInvoke.IsWindowVisible(_window))
        {
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOW);
        }

        var foreground = PInvoke.SetForegroundWindow(_window);
        _log.Step($"relaunch with {arguments} arguments: window shown, foreground {(foreground ? "taken" : "refused")}");
    }

    /// <summary>
    /// Plan section 3, "Teardown": mark closing, let the page finish its close work, stop admitting calls and let the
    /// cancelled ones settle (bounded, still pumping), close the controller on this thread, and destroy the window. A
    /// page that does not answer gets no close work, and its renderer is ended before the controller closes. At session end
    /// <paramref name="deadline"/>, a <see cref="Stopwatch"/> timestamp, cuts both waits short.
    /// </summary>
    public async Task TeardownAsync(int exitCode, long? deadline = null)
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

        if (_bridge.Task.IsCompletedSuccessfully)
        {
            var bridge = _bridge.Task.Result;
            if (!pageHung)
            {
                await bridge.RunPageCloseWorkAsync(Bounded(PageCloseWorkMilliseconds, deadline));
            }

            var settled = bridge.CloseAsync("window closing");
            _log.Step("calls stopped");
            var first = await Task.WhenAny(settled, Task.Delay(Bounded(CommandSettleMilliseconds, deadline)));
            _log.Step(first == settled ? "calls settled" : "calls still running at the limit");
        }

        _stopping.Cancel();
        if (pageHung)
        {
            _ = _view?.TerminateRenderers();
        }

        _view?.Close();
        _log.Step("controller closed");
        if (_sessionEnding)
        {
            // Windows ended the process as soon as the window holding WM_ENDSESSION was gone, before the services' last
            // lines reached the log (a real sign-out in Windows Sandbox, 2026-11-19). It is destroyed once the message
            // returns; Windows' documentation says an ending session need not destroy windows at all.
            _tornDown = true;
            _log.Step("window kept until WM_ENDSESSION returns");
            return;
        }

        PInvoke.DestroyWindow(_window);
        _log.Step("window destroyed");
    }

    /// <summary>
    /// After the window is gone, off the UI thread: the bridge's reader, then what the logs still count. The app's
    /// services are disposed by the run after this.
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            if (_bridge.Task.IsCompletedSuccessfully)
            {
                await _bridge.Task.Result.StopAsync();
            }

            // What is still counted is written before the log closes.
            if (_violations is not null)
            {
                await _violations.DisposeAsync();
            }

            if (_pageErrors is not null)
            {
                await _pageErrors.DisposeAsync();
            }
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    /// <summary>Once the services exist: the close guard, the hang hook, and the bridge the WebView waits for.</summary>
    private async Task ConnectAsync(UiThread ui, Task<SparRunningApp> built)
    {
        try
        {
            _running = await built;
        }
        catch (Exception exception)
        {
            _log.Error("The app could not start", exception);
            Fail(StartFailedExitCode, "The app could not start: " + exception.Message);
            return;
        }

        _guard = new CloseGuard(_app.Builder.CloseGuard, _running.Services, _log.Logger);
        _hangHook.Start(_running.Services);
        var pageLogger = _running.Services.GetService<ILoggerFactory>()?.CreateLogger(PageErrorLog.Category);
        _pageErrors = new PageErrorLog(pageLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TimeProvider.System);
        var bridge = new WebViewBridge(
            ui,
            _log,
            _running.Dispatcher,
            _running.Catalog.Fingerprint,
            SparDevelopment.IsSupported,
            _listenerReports ? _violations : null,
            _pageErrors,
            new HostCommands(_running.Folders.DataRoot, ChooseSchemeAsync, _log.Logger));
        if (_view is { } view && _state != State.Closing)
        {
            _watch = WindowsPageHost.Watch(ui, _log, _window, _title, bridge, view, _hangHook, _automated, HeartbeatInterval());
            _watch.Start();
            if (SparDevelopment.IsSupported)
            {
                DevelopmentTriggers.Watch(ui, bridge, view, _log);
                DevelopmentInput.Watch(bridge, view, _log);
            }
        }

        _bridge.TrySetResult(bridge);
        if (_state == State.Starting)
        {
            _state = State.Running;
        }
    }

    /// <summary>
    /// An unrecoverable failure: a message owned by the window, then the teardown with its exit code. In the automated
    /// mode nobody can answer, so the message goes to the log only.
    /// </summary>
    private void Fail(int exitCode, string message) => _ui?.Post(() =>
    {
        if (_state == State.Closing || _failed)
        {
            return;
        }

        _failed = true;
        _exitCode = exitCode;
        _log.Error($"Exiting with code {exitCode}: {message}");
        if (!_automated)
        {
            NativeDialog.ShowError(_window, _title, message);
        }

        _ = TeardownAsync(exitCode);
    });

    /// <summary>
    /// The Runtime failed at start (exit 3 to 5): the start-up report, with the app's hook, owned by the window, which
    /// keeps pumping meanwhile; then the teardown.
    /// </summary>
    private void StartupFailed(StartupProblem problem) => _ui?.Post(() => _ = ReportStartupFailureAsync(problem));

    private async Task ReportStartupFailureAsync(StartupProblem problem)
    {
        if (_state == State.Closing || _failed)
        {
            return;
        }

        _failed = true;
        _exitCode = problem.ExitCode;
        await StartupFailure.ReportAsync(problem, Startup, _window);
        await TeardownAsync(problem.ExitCode);
    }

    /// <summary>The policy the page gets, in the log once it is composed (off the UI thread).</summary>
    private async Task LogPolicyAsync(PageServer pages)
    {
        try
        {
            var policy = await pages.Policy;
            _log.Info($"Content-Security-Policy: {policy.ContentSecurityPolicy}");
            _log.Info($"Permissions-Policy: {policy.PermissionsPolicy}");
        }
        catch (Exception exception)
        {
            _log.Error("The page could not be prepared", exception);
        }
    }

    /// <summary><c>WM_ERASEBKGND</c>: the theme's colour, never the default white (plan 3d's first layer, with the class brush).</summary>
    private unsafe LRESULT? PaintBackground(HWND window, WPARAM wParam)
    {
        if (_background.IsNull || !PInvoke.GetClientRect(window, out var client))
        {
            return null;
        }

        _ = PInvoke.FillRect(new HDC((nint)wParam.Value), &client, _background);
        return new LRESULT(1);
    }

    /// <summary>A change of the system's theme, when the window follows it: window colour and title bar change together.</summary>
    private unsafe void OnSettingChange(HWND window, LPARAM lParam)
    {
        if (_theme is null || lParam.Value == 0 || new string((char*)lParam.Value) != "ImmersiveColorSet" || !_theme.SystemChanged())
        {
            return;
        }

        Repaint(window, _theme);
        _log.Step($"system theme changed to {(_theme.IsDark ? "dark" : "light")}");
    }

    /// <summary>
    /// The page's <c>setColorScheme</c> (any thread): the window takes the choice on its UI thread, as it takes a change
    /// of the system's theme, and the task completes once it has. The bridge's host command saved it already.
    /// </summary>
    private Task ChooseSchemeAsync(bool? dark)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_ui is not { } ui)
        {
            applied.TrySetResult();
            return applied.Task;
        }

        ui.Post(() => _ = ApplySchemeAsync(dark, applied), "colour scheme");
        return applied.Task;
    }

    /// <summary>
    /// The window colour, the title bar and the page's scheme (the profile's <c>PreferredColorScheme</c>, which
    /// <c>prefers-color-scheme</c> follows), together (UI thread). A test window also emulates the scheme on the page.
    /// </summary>
    private async Task ApplySchemeAsync(bool? dark, TaskCompletionSource applied)
    {
        try
        {
            if (_theme is not { } theme || _state == State.Closing || _window.IsNull)
            {
                return;
            }

            var changed = theme.Choose(dark);
            _view?.SetPageScheme(theme.PageScheme);
            if (changed)
            {
                Repaint(_window, theme);
            }

            _log.Step($"colour scheme chosen by the page: {SavedColorScheme.NameOf(dark)}, the window {(theme.IsDark ? "dark" : "light")}");
            if (SparTestSession.IsSupported && _source?.ColorSchemeChosen is { } chosen)
            {
                await chosen(theme.IsDark);
            }
        }
        catch (Exception exception)
        {
            _log.Error("The colour scheme could not be applied", exception);
        }
        finally
        {
            applied.TrySetResult();
        }
    }

    /// <summary>The window colour and the title bar in the theme's scheme now (UI thread).</summary>
    private unsafe void Repaint(HWND window, WindowTheme theme)
    {
        DeleteBackground();
        _background = PInvoke.CreateSolidBrush(new COLORREF(theme.Background.ColorRef));
        theme.ApplyTitleBar(window);
        PInvoke.InvalidateRect(window, (RECT*)null, true);
    }

    private void DeleteBackground()
    {
        if (!_background.IsNull)
        {
            PInvoke.DeleteObject(_background);
            _background = HBRUSH.Null;
        }
    }

    /// <summary>The heartbeat's interval: 5 s, or a Development build's override (the call is trimmed from Production).</summary>
    private static int HeartbeatInterval() =>
        SparDevelopment.IsSupported ? DevelopmentTriggers.HeartbeatInterval() : PageHeartbeat.IntervalMilliseconds;

    /// <summary>The window messages Spar handles, named for the stall probe; null for those it leaves to Windows.</summary>
    private static string? MessageName(uint message) => message switch
    {
        PInvoke.WM_ERASEBKGND => "WM_ERASEBKGND",
        PInvoke.WM_SIZE => "WM_SIZE",
        PInvoke.WM_WINDOWPOSCHANGED => "WM_WINDOWPOSCHANGED",
        PInvoke.WM_SETFOCUS => "WM_SETFOCUS",
        PInvoke.WM_GETMINMAXINFO => "WM_GETMINMAXINFO",
        PInvoke.WM_DPICHANGED => "WM_DPICHANGED",
        PInvoke.WM_SETTINGCHANGE => "WM_SETTINGCHANGE",
        PInvoke.WM_CLOSE => "WM_CLOSE",
        PInvoke.WM_DESTROY => "WM_DESTROY",
        _ => null,
    };

    /// <summary>The WebView is transparent over the window colour (plan 3d); only a Development or Test build may say otherwise.</summary>
    private static bool TransparentWebView() =>
        !(SparDevelopment.IsSupported && Environment.GetEnvironmentVariable(WebViewBackgroundVariable) == "default");

    private enum State
    {
        Starting,
        Running,
        Closing,
    }
}
