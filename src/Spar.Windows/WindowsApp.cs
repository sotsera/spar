using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Runtime;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// One run of the app on Windows. Before anything else, the start-up of plan section 3: the Windows check, single
/// instance (a later launch hands its arguments to the running one and exits), and the WebView2 Runtime check, each
/// failure reported with its fixed exit code. Then the window, shown at once in the theme's colour (plan 3d), the app's
/// services built beside it, the WebView, and the two-stage close (decision "Two-stage close"): every close of the
/// window is a close request, the page is checked and the backend's guard decides or asks, and only an approved
/// request starts the teardown. While it runs, the page's heartbeat watches for a hung page (<see cref="PageWatch"/>),
/// and from the teardown on, <see cref="ShutdownWatch"/> watches for a shutdown that does not finish.
/// </summary>
internal sealed class WindowsApp : IWindowMessages, IReportsErrors
{
    /// <summary>A Development or Test build may set it to <c>default</c> to keep the WebView's own background (measurements).</summary>
    public const string WebViewBackgroundVariable = "SPAR_DEV_WEBVIEW_BACKGROUND";

    /// <summary>
    /// The exit code when the app could not start for a reason of its own: its services could not be built, as in a test
    /// host, or its configuration cannot be used, such as a profile folder on a network drive.
    /// </summary>
    private const int StartFailedExitCode = 70;

    /// <summary>The longest AppUserModelID Windows takes.</summary>
    private const int MaxAppUserModelId = 128;

    /// <summary>How long the page's close work may take (plan section 2c, "Flush before close").</summary>
    private const int PageCloseWorkMilliseconds = 1_000;

    /// <summary>How long teardown waits for cancelled commands to end before closing the WebView anyway.</summary>
    private const int CommandSettleMilliseconds = 3_000;

    /// <summary>How long the process waits at exit for a relaunch handler that is still running.</summary>
    private const int RelaunchSettleMilliseconds = 3_000;

    /// <summary>How long the process waits at exit for the page hang hook to see its last change.</summary>
    private const int HookSettleMilliseconds = 3_000;

    private readonly SparApp _app;
    private readonly HostLog _log = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<WebViewBridge> _bridge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _automated = SparDevelopment.IsAutomatedFromEnvironment();
    private readonly string _title;
    private readonly SparFolders _folders;
    private readonly BrowserProfile _profile;
    private readonly string _identity;
    private readonly RelaunchQueue _relaunches;
    private readonly PageHangHook _hangHook;
    private CspViolationLog? _violations;
    private bool _listenerReports;
    private SingleInstance? _instance;
    private WindowTheme? _theme;
    private SparRunningApp? _running;
    private CloseGuard? _guard;
    private UiThread? _ui;
    private BrowserView? _view;
    private PageWatch? _watch;
    private ShutdownWatch? _shutdown;
    private HWND _window;
    private HBRUSH _background;
    private State _state;
    private bool _closeRequestOpen;
    private bool _failed;
    private int _exitCode;

    /// <summary>What a start-up report needs from this run.</summary>
    private StartupContext Startup => new(_title, _app.Builder.StartupProblemHandler, _log, _automated);

    public WindowsApp(SparApp app)
    {
        _app = app;
        _title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
        _identity = SparFolders.ChannelId(app.AppId, SparDevelopment.IsSupported);
        _folders = SparFolders.ForApp(app.AppId, SparDevelopment.IsSupported, app.Builder.FolderOptions);
        _profile = new BrowserProfile(_folders.Profile);
        _relaunches = new RelaunchQueue(app.Builder.RelaunchHandler, _log.Logger);
        _hangHook = new PageHangHook(app.Builder.PageHangHandler, _log.Logger);
    }

    public int Run()
    {
        _log.UseFile(_folders.Logs);
        _log.Info($"Host started {HostLog.ProcessAgeMilliseconds():0.0} ms after the process.");
        try
        {
            if (Configure() is { } configurationFailed)
            {
                return configurationFailed;
            }

            return CheckThenRun();
        }
        finally
        {
            // The pipe closes before the lock is released, so no launch is acknowledged by a closing app.
            _instance?.Dispose();
            _shutdown?.Complete();
            _log.Close();
        }
    }

    public void Report(Exception exception) => _log.Error("The window's thread failed", exception);

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
            case WindowsMessages.QueryEndSession:
                // Spar never blocks Windows from logging off (decision "Two-stage close").
                return new LRESULT(1);
            case PInvoke.WM_DESTROY:
                DeleteBackground();
                PInvoke.PostQuitMessage(0);
                return default(LRESULT);
            default:
                return null;
        }
    }

    /// <summary>
    /// Before the start-up checks (plan section 3c): the app's identity to the OS, the browser opt-ins in force, each in
    /// the log, the WebView2 environment variables the app did not allow removed before the loader's first call, and the
    /// app's folder choices checked. Returns an exit code when the configuration stops the start.
    /// </summary>
    private int? Configure()
    {
        var builder = _app.Builder;
        _log.Info($"App id {_identity}, folder {_folders.DataRoot}");
        SetIdentity();
        if (builder.BrowserOptions.Active() is { Length: > 0 } active)
        {
            _log.Warn($"Browser opt-ins in force: {active}");
        }

        BrowserEnvironment.Filter(builder.BrowserOptions.EnvironmentOverrides, _log);
        if (SparFolders.ProfileProblem(builder.FolderOptions.ProfileFolder) is { } problem)
        {
            return StartupFailure.ReportConfiguration(problem, StartFailedExitCode, Startup);
        }

        return null;
    }

    /// <summary>
    /// The process's explicit AppUserModelID, the app id of this channel (decision "M1b's remaining questions without a
    /// spike"): the taskbar groups the app's windows by it, a Development build apart from an installed one, and
    /// WebView2 looks an administrator's policies up by it. It registers nothing with the system. Windows takes at most
    /// 128 characters; the build caps an app id at 116, so even its Development id fits, and this check only guards a
    /// catalog built without Spar's build steps.
    /// </summary>
    private void SetIdentity()
    {
        if (_identity.Length > MaxAppUserModelId || PInvoke.SetCurrentProcessExplicitAppUserModelID(_identity).Failed)
        {
            _log.Warn("AppUserModelID not set: the app id is too long or was refused.");
        }
    }

    /// <summary>
    /// Plan section 3's order: Windows, then single instance (before anything touches the data folder's browser
    /// profile, and before the slower Runtime check, so a relaunch hands over at once), then the Runtime.
    /// </summary>
    private int CheckThenRun()
    {
        if (Prerequisites.CheckWindows() is { } oldWindows)
        {
            return StartupFailure.Report(oldWindows, Startup);
        }

        var builder = _app.Builder;
        var claim = SingleInstance.Claim(_folders.DataRoot, new Relaunch(builder.Args, Environment.CurrentDirectory), _log);
        if (claim.Problem is { } unreachable)
        {
            return StartupFailure.Report(unreachable, Startup);
        }

        if (claim.Instance is null)
        {
            _log.Step("exit 0 (handed to the running instance)");
            return 0;
        }

        _instance = claim.Instance;
        _log.Phase("single instance");

        if (Prerequisites.CheckRuntime() is { } runtime)
        {
            return StartupFailure.Report(runtime, Startup);
        }

        _log.Phase("prerequisites checked");
        var theme = WindowTheme.Read(builder.MainWindow, _folders.DataRoot);
        _theme = theme;
        var built = Task.Run(() => Build(_folders));

        UiThread.Run(theme.Background.ColorRef, Report, ui => Start(ui, theme, built));

        Stop();
        _log.Step($"exit {_exitCode}");
        return _exitCode;
    }

    /// <summary>Builds the app's services on the thread pool while the window and the WebView start.</summary>
    private SparRunningApp Build(SparFolders folders)
    {
        var builder = _app.Builder;
        // The profile folders no file grant may reach, with the folder in use once the environment exists.
        builder.Services.AddSingleton(_profile);
        SparRunningApp.AddSparServices(builder.Services, _app.Catalog, folders, builder.BridgeOptions.Freeze());
        if (SparDevelopment.IsSupported)
        {
            DevelopmentTriggers.AddSlowDisposal(builder.Services, _log);
        }

        var running = SparRunningApp.Build(builder.Services, _app.Catalog, folders);
        if (SparDevelopment.IsSupported)
        {
            DevelopmentTriggers.CreateSlowDisposal(running.Services);
        }

        _log.UseAppLogging(running.Services);
        _log.Phase("services built");
        return running;
    }

    /// <summary>
    /// On the UI thread: the window first, in the theme's colour and title bar before it is shown (plan 3d), then the
    /// relaunch pipe, then everything that takes time, without blocking the thread.
    /// </summary>
    private void Start(UiThread ui, WindowTheme theme, Task<SparRunningApp> built)
    {
        _ui = ui;
        if (_app.Builder.DiagnosticsOptions.CreateStallProbe(_log.Logger) is { } probe)
        {
            ui.UseProbe(probe);
            _log.Info("UI-thread stall probe on.");
        }

        _background = PInvoke.CreateSolidBrush(new COLORREF(theme.Background.ColorRef));
        var bounds = WindowPlacement.Initial(_app.Builder.MainWindow.InitialSize);
        _window = ui.CreateWindow(_title, WindowPlacement.Style, bounds, this);
        theme.ApplyTitleBar(_window);
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWDEFAULT);
        _log.Phase("window shown");
        _log.Info($"Theme {(theme.IsDark ? "dark" : "light")} ({(theme.AppChoosesDark is null ? "system" : "app's choice")}), window {theme.Background}.");

        _instance?.Listen(OnRelaunch, _log);

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
        _ = LogPolicyAsync(pages);
        var guard = new PageGuard(_log, security, _app.Builder.BrowserOptions.HistoryNavigation);
        var defaults = new BrowserDefaults(_log, _app.Builder.BrowserOptions, development, _automated);
        var script = PageScript.Text(_listenerReports);
        _view = new BrowserView(
            ui, _log, _window, _profile, defaults, pages, guard, script, _bridge.Task, theme, TransparentWebView(), Fail, StartupFailed);
        _ = _view.StartAsync();
        _ = ConnectAsync(ui, built);
    }

    /// <summary>Once the services exist: the close guard, the relaunch handler, and the bridge the WebView waits for.</summary>
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
        _relaunches.Start(_running.Services);
        _hangHook.Start(_running.Services);
        var bridge = new WebViewBridge(
            ui, _log, _running.Dispatcher, _running.Catalog.Fingerprint, SparDevelopment.IsSupported, _listenerReports ? _violations : null);
        if (_view is { } view && _state != State.Closing)
        {
            _watch = new PageWatch(ui, _log, _window, _title, bridge, view, _hangHook, _automated, HeartbeatInterval());
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
    /// A later launch's arguments, on the pipe's task (decision "The relaunch handler"): the window is shown and focused
    /// on the UI thread, then the relaunch joins the handler's queue. True acknowledges it; a closing app refuses it.
    /// </summary>
    private bool OnRelaunch(Relaunch relaunch)
    {
        var ui = _ui;
        if (ui is null || _state == State.Closing)
        {
            return false;
        }

        ui.Post(() =>
        {
            if (_state == State.Closing)
            {
                return;
            }

            BringForward(relaunch.Arguments.Count);
            _relaunches.TryAdd(relaunch);
        });
        return true;
    }

    /// <summary>Shows and restores the main window and asks for the foreground, which the relaunching process allowed.</summary>
    private void BringForward(int arguments)
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

    private void OnCloseMessage()
    {
        if (_state == State.Closing || _closeRequestOpen)
        {
            // A second click joins the open request; during teardown there is nothing left to ask.
            return;
        }

        _ = CloseRequestedAsync(CloseReason.User);
    }

    /// <summary>
    /// The two-stage close: the page's guard, then the backend's, each closing, keeping or asking. Only an approved
    /// request starts the teardown.
    /// </summary>
    private async Task CloseRequestedAsync(CloseReason reason)
    {
        _closeRequestOpen = true;
        try
        {
            var request = new CloseRequest("main", reason, _automated);
            _log.Step($"close requested ({reason}{(_automated ? ", automated" : "")})");

            // The page first: a page that does not answer within about 5 s, or is already hung, gets "Wait / Close
            // anyway". The page's own guard (windows.onCloseRequested) is asked here too when it is built; its "keep"
            // will end the request before the backend is asked.
            var page = _watch is null ? PageCloseAnswer.Agree : await _watch.AskBeforeCloseAsync();
            if (_state == State.Closing)
            {
                return;
            }

            var verdict = _guard is null ? CloseVerdict.Close : await _guard.DecideAsync(request, _stopping.Token);
            if (verdict.Kind == CloseVerdictKind.Keep)
            {
                _log.Step("close kept by the backend's guard");
                _watch?.CloseRequestKept();
                return;
            }

            if (verdict.Kind == CloseVerdictKind.Ask && !NativeDialog.ConfirmClose(_window, _title, verdict.Question!))
            {
                _log.Step("close kept by the user");
                _watch?.CloseRequestKept();
                return;
            }

            if (page == PageCloseAnswer.CloseAnyway)
            {
                _watch?.ReportClosedAnyway();
            }

            await TeardownAsync(0);
        }
        catch (Exception exception)
        {
            _log.Error("The close request failed", exception);
        }
        finally
        {
            _closeRequestOpen = false;
        }
    }

    /// <summary>
    /// Plan section 3, "Teardown": mark closing and close the relaunch pipe, let the page finish its close work, stop
    /// admitting calls and let the cancelled ones settle (bounded, still pumping), close the controller on this thread,
    /// and destroy the window. The process then exits without waiting for the browser. A page that does not answer gets
    /// no close work, and its renderer is ended before the controller closes. From here on the shutdown is watched, and
    /// after about 10 s the user may end it (<see cref="ShutdownWatch"/>).
    /// </summary>
    private async Task TeardownAsync(int exitCode)
    {
        if (_state == State.Closing)
        {
            return;
        }

        _state = State.Closing;
        _shutdown = ShutdownWatch.Start(_title, _log, _automated, ShutdownHang.FirstPromptMilliseconds, ShutdownHang.AskAgainMilliseconds);
        if (!_failed)
        {
            _exitCode = exitCode;
        }

        _instance?.StopListening();
        _log.Step("closing");
        var pageHung = _watch?.IsUnresponsive == true;
        _watch?.Stop();

        if (_bridge.Task.IsCompletedSuccessfully)
        {
            var bridge = _bridge.Task.Result;
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
            _ = _view?.TerminateRenderers();
        }

        _view?.Close();
        _log.Step("controller closed");
        PInvoke.DestroyWindow(_window);
        _log.Step("window destroyed");
    }

    /// <summary>An unrecoverable failure: a message owned by the window, then the teardown with its exit code.</summary>
    private void Fail(int exitCode, string message) => _ui?.Post(() =>
    {
        if (_state == State.Closing || _failed)
        {
            return;
        }

        _failed = true;
        _exitCode = exitCode;
        _log.Error($"Exiting with code {exitCode}: {message}");
        NativeDialog.ShowError(_window, _title, message);
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

    /// <summary>After the window's thread has ended: relaunches, the bridge's reader, then the app's services.</summary>
    private void Stop()
    {
        try
        {
            // This is the process's main thread; the window's thread has already ended, so waiting blocks nothing.
            if (!_relaunches.StopAsync().Wait(RelaunchSettleMilliseconds))
            {
                _log.Warn("A relaunch handler was still running at exit.");
            }

            if (!_hangHook.StopAsync(TimeSpan.FromMilliseconds(HookSettleMilliseconds)).GetAwaiter().GetResult())
            {
                _log.Warn("The page hang hook was still running at exit: its token was cancelled.");
            }

            if (_bridge.Task.IsCompletedSuccessfully)
            {
                _bridge.Task.Result.StopAsync().GetAwaiter().GetResult();
            }

            // What is still counted is written before the log closes.
            _violations?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            _running?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _log.Step("services disposed");
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly", exception);
        }
        finally
        {
            _stopping.Dispose();
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

        DeleteBackground();
        _background = PInvoke.CreateSolidBrush(new COLORREF(_theme.Background.ColorRef));
        _theme.ApplyTitleBar(window);
        PInvoke.InvalidateRect(window, (RECT*)null, true);
        _log.Step($"system theme changed to {(_theme.IsDark ? "dark" : "light")}");
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

    /// <summary>Messages CsWin32 is not asked to generate.</summary>
    private static class WindowsMessages
    {
        public const uint QueryEndSession = 0x0011;
    }
}

/// <summary>
/// The Windows host as Spar's generated code creates it (<see cref="ISparHost"/>): one window, its WebView and the
/// app's backend. Apps never reference it; Spar.Sdk adds it for <c>SparPlatforms</c> that include windows.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class SparWindowsHost : ISparHost
{
    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new WindowsApp(app).Run();
    }
}
