using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Spar.AppWindows;
using Spar.Files;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Runtime;

namespace Spar.Linux;

/// <summary>What the run that owns a window does at the window's turning points (loop thread).</summary>
internal interface IAppWindowOwner
{
    /// <summary>The teardown started: an app starts watching its shutdown and stops taking relaunches.</summary>
    void TeardownStarted();

    /// <summary>The window is gone and its web processes have exited: an app ends its main loop.</summary>
    void WindowClosed();

    /// <summary>
    /// The WebKit processes this window waits for after its destruction (off the loop): an app's are every one below its
    /// process; a test window's only its own page's, since other tests' windows share the test process.
    /// </summary>
    List<(int Id, string Name)> WebKitProcesses();
}

/// <summary>
/// The app's main window on Linux: a GTK 4 window with the WebKit view (<see cref="BrowserView"/>), the bridge to the
/// app's services (<see cref="ScriptBridge"/>), and the two-stage close (decision "Two-stage close"): every close of
/// the window is a close request, the backend's guard decides or asks, and only an approved request starts the
/// teardown; that part is in <c>AppWindow.Closing.cs</c>. The order at start is plan section 3's: the view with its
/// session, attached to the window, loading, then the window presented at once. Everything here runs on the main loop
/// unless a member says otherwise.
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>The exit code when the app could not start for a reason of its own, such as its services failing to build.</summary>
    public const int StartFailedExitCode = 70;

    /// <summary>The exit code when the page cannot be recovered (plan section 3, "Missing prerequisites": broken).</summary>
    public const int BrokenExitCode = 5;

    private readonly SparApp _app;
    private readonly SparPageSecurity _security;
    private readonly HostLog _log;
    private readonly bool _automated;
    private readonly string _title;
    private readonly SparFolders _folders;
    private readonly BrowserProfile _profile;
    private readonly PageHangHook _hangHook;
    private readonly ConcurrentQueue<string>? _promptAnswers;
    private readonly IAppWindowOwner _owner;
    private readonly BrowserSource? _source;
    private readonly OpenWindow? _opened;
    private readonly AppWindowSet? _windows;
    private readonly nint _sharedSession;
    private readonly CancellationTokenSource _stopping = new();
    private readonly WindowDialogs _dialogs = new();
    private CloseGuard? _guard;
    private MainLoop? _ui;
    private SchemeServer? _pages;
    private ScriptBridge? _bridge;
    private BrowserView? _view;
    private CspViolationLog? _violations;
    private PageWatch? _watch;
    private WindowTheme? _theme;
    private GCHandle _self;
    private nint _window;
    private FileDialogHost? _fileDialogs;
    private nuint _closeHandler;
    private State _state;
    private bool _failed;
    private int _exitCode;
    private Spar.DevServer.DevServerSession? _devServer;
    private Spar.Devtools.DevtoolsHost? _devtools;

    /// <param name="security">The page security in force, resolved before the engine started (it sets WebAssembly).</param>
    /// <param name="promptAnswers">A Development build's scripted answers to the hung-page dialogs; null otherwise.</param>
    /// <param name="source">A test window's display and page driver (the Window surface); null for an app's window.</param>
    /// <param name="opened">A window of one of the app's kinds (decision "Secondary and modal windows"); null for the main window.</param>
    /// <param name="windows">The app's windows, which the page may open more of; null where it opens none.</param>
    /// <param name="sharedSession">The main window's network session, for a window of the app's kinds; 0 for the main window.</param>
    public AppWindow(
        SparApp app,
        SparPageSecurity security,
        HostLog log,
        bool automated,
        string title,
        SparFolders folders,
        BrowserProfile profile,
        PageHangHook hangHook,
        ConcurrentQueue<string>? promptAnswers,
        IAppWindowOwner owner,
        BrowserSource? source = null,
        OpenWindow? opened = null,
        AppWindowSet? windows = null,
        nint sharedSession = 0)
    {
        _app = app;
        _security = security;
        _log = log;
        _automated = automated;
        _title = title;
        _folders = folders;
        _profile = profile;
        _hangHook = hangHook;
        _promptAnswers = promptAnswers;
        _owner = owner;
        _source = source;
        _opened = opened;
        _windows = windows;
        _sharedSession = sharedSession;
    }

    /// <summary>The window's id in the app: <c>main</c>, or its kind's name with a number.</summary>
    public string Id => _opened?.Id ?? WindowRoutes.Main;

    /// <summary>The view's network session, which the app's other windows share (loop thread); 0 before the start.</summary>
    public nint Session => _view?.Session ?? 0;

    /// <summary>The run's exit code: 0, or the code of the failure that ended it.</summary>
    public int ExitCode => _exitCode;

    /// <summary>Whether the teardown has started.</summary>
    public bool IsClosing => _state == State.Closing;

    /// <summary>The GTK window; 0 before the start and once it is destroyed (loop thread).</summary>
    public nint Handle => _window;

    /// <summary>
    /// A test window's size from its content (a scene's fixed viewport): the default size goes, so the window is the
    /// smallest that holds the view's size request and the title bar (loop thread, before it is shown).
    /// </summary>
    internal void FitToContent() => Gtk.gtk_window_set_default_size(_window, 1, 1);

    /// <summary>The WebKit view; 0 before the start and from the teardown on (loop thread).</summary>
    public nint ViewHandle => _view?.Handle ?? 0;

    public void Report(Exception exception) => _log.Error("The window's thread failed", exception);

    /// <summary>The window's options: its kind's, or the main window's.</summary>
    private WindowOptions Options => _opened?.Options ?? _app.Builder.MainWindow;

    /// <summary>The window's name in hooks and logs: <c>main</c>, or its kind.</summary>
    private string Name => _opened?.Kind ?? WindowRoutes.Main;

    /// <summary>Where the window's page starts: the app's origin at the kind's route, or its root.</summary>
    private string StartAddress => SchemeServer.Origin + Options.PageRoute;

    /// <summary>
    /// A Debug build's development server, which the page comes from (before <see cref="Start"/>). Called only behind
    /// <see cref="Spar.DevServer.DevServerSettings.IsSupported"/>, so no other build has it.
    /// </summary>
    public void UseDevServer(Spar.DevServer.DevServerSession devServer) => _devServer = devServer;

    /// <summary>The devtools of a Development build: its page automation goes into the view before the first load.</summary>
    public void UseDevtools(Spar.Devtools.DevtoolsHost devtools) => _devtools = devtools;

    /// <summary>
    /// The window and its view, without blocking the loop: the theme's layers, the view with its session and the
    /// relay, attached and loading, the window presented in the theme's colour, then the bridge's services once they are
    /// built (they are being built meanwhile).
    /// </summary>
    public unsafe void Start(MainLoop ui, WindowTheme theme, Task<SparRunningApp> built)
    {
        _ui = ui;
        AttachHandle();
        _theme = theme;
        _self = GCHandle.Alloc(this);
        var options = Options;
        _window = Gtk.gtk_window_new();
        var modalOwner = JoinGroup();
        theme.Apply(_window);
        _log.Info($"Theme {(theme.IsDark ? "dark" : "light")}, {theme.Background}, from {theme.Source}.");
        Gtk.gtk_window_set_title(_window, _title);
        var saved = ApplySavedSize();
        if (options.Minimum is { Width: > 0 } or { Height: > 0 })
        {
            Gtk.gtk_widget_set_size_request(_window, options.Minimum.Width, options.Minimum.Height);
        }

        _closeHandler = GLib.g_signal_connect_data(
            _window, "close-request", (delegate* unmanaged<nint, nint, int>)&OnCloseRequest, GCHandle.ToIntPtr(_self), 0, 0);

        var frontend = _app.Catalog.AppAssembly is { } assembly ? SparFrontend.Open(assembly) : null;
        var security = _security;
        var development = SparDevelopment.IsSupported;
        var endpointReports = PagePolicy.Reports(security.Endpoint, development);
        var listenerReports = PagePolicy.Reports(security.Listener, development);
        if (endpointReports || listenerReports)
        {
            // The advice only in Development: behind the feature switch, so the trimmer removes it from Production.
            _violations = new CspViolationLog(_log.Logger, TimeProvider.System, SparDevelopment.IsSupported ? CspAdvice.For : null);
        }

        var routes = _app.Builder.WindowKinds.Values.Select(kind => kind.PageRoute).Distinct().ToList();
        _pages = new SchemeServer(frontend, ui, _log, security, development, endpointReports ? _violations : null, routes);
        if (Spar.DevServer.DevServerSettings.IsSupported && _devServer is { } devServer)
        {
            // A development server that stops takes the page to the error page, which offers Retry.
            _pages.UseDevServer(devServer, security);
            devServer.Stopped += () => ui.Post(
                () =>
                {
                    if (_state != State.Closing && _view?.Handle is { } view and not 0)
                    {
                        WebKit.webkit_web_view_load_uri(view, StartAddress);
                    }
                },
                "dev server stopped");
        }

        _ = LogPolicyAsync(_pages);
        _bridge = new ScriptBridge(ui, _log, built, development, listenerReports ? _violations : null, ChooseSchemeAsync, app => HostWindow(ui, app));
        var browser = _app.Builder.BrowserOptions;
        var guard = new PageGuard(_log, security, browser.HistoryNavigation);
        var defaults = new BrowserDefaults(_log, browser, development, _automated);
        defaults.KeepGtkInspectorClosed(_window);
        _view = new BrowserView(ui, _log, _pages, _bridge, guard, defaults, _profile, Fail, _source)
        {
            StartAddress = StartAddress,
            SharedSession = _sharedSession,
            Zoom = (Options.ZoomLimits.Enabled, Options.ZoomLimits.Min, Options.ZoomLimits.Max, Placements?.Get(Options)?.Zoom ?? 1),
            ZoomChanged = OnZoomChanged,
        };
        if (Spar.Devtools.SparDevtools.IsSupported && _devtools is { } devtools
            && Spar.Devtools.DevtoolsHost.EngineScript(_app.Catalog.AppAssembly) is { } engine)
        {
            _view.DevtoolsInstall = (view, content) => Devtools.DevtoolsPage.Install(ui, view, content, devtools, engine);
        }

        // The optional telemetry layer: its routes on the page's origin, and its settings as a script of the page's world.
        var telemetry = Spar.Telemetry.TelemetryRoute.For(_app.Builder);
        _pages.UseTelemetry(telemetry);
        _pages.UseBlobs(built);
        _view.Create(_folders.Profile, CacheFolder(_folders), ScriptBridge.RelayScript(listenerReports), theme.Background, telemetry.PageScript());

        // The heartbeat follows the view's documents from the first one on, before the services exist.
        _watch = LinuxPageHost.Watch(
            ui, _log, () => _window, _dialogs, _title, _bridge, _view, _hangHook, _automated, HeartbeatInterval(), _promptAnswers);
        _watch.Start();
        if (SparDevelopment.IsSupported)
        {
            DevelopmentTriggers.Watch(ui, _bridge, _view, _log);
        }

        Gtk.gtk_window_set_child(_window, _view.Handle);
        _view.Load();
        theme.Realize();
        PlaceOnScreen(modalOwner, saved);
        if (SparTestSession.IsSupported && _source?.Realized is { } realized)
        {
            realized(_window);
        }

        Gtk.gtk_window_present(_window);
        _log.Phase(modalOwner is null ? "window shown" : $"window shown, modal to {modalOwner.Id}");
        FollowPlacement();
        theme.Follow(() =>
        {
            _view?.SetBackground(theme.Background);
            _log.Info($"Theme changed to {(theme.IsDark ? "dark" : "light")} by the system.");
        });
        _ = ConnectAsync(built);
    }

    /// <summary>
    /// A relaunch reached this instance (loop thread): the window is shown and presented with the relaunch's
    /// activation token, so the window manager lets it take the focus.
    /// </summary>
    public void BringForward(int argumentCount, string? activationToken)
    {
        if (_window == 0 || _state == State.Closing)
        {
            return;
        }

        if (activationToken is { Length: > 0 } token)
        {
            Gtk.gtk_window_set_startup_id(_window, token);
        }

        Gtk.gtk_window_present(_window);
        _log.Info($"Relaunch with {argumentCount} arguments: the window was presented{(activationToken is null ? "" : " with its activation token")}.");
    }

    /// <summary>A single-instance kind opened again: its window is presented (loop thread).</summary>
    public void Focus()
    {
        if (_window == 0 || _state == State.Closing)
        {
            return;
        }

        Gtk.gtk_window_present(_window);
        _log.Step($"window {Id} presented");
    }

    /// <summary>
    /// After the window is gone, off the main loop: the bridge's reader. The app's services are disposed by the run
    /// after this, and <see cref="Release"/> follows once the loop has ended.
    /// </summary>
    public async Task StopAsync()
    {
        // Off the main loop: the windows' places still pending go to windows.json now.
        Placements?.Flush();

        if (_bridge is { } bridge)
        {
            await bridge.StopAsync();
        }

        if (_violations is { } violations)
        {
            // What the reports still counted, and the run's totals, before the services and the log go.
            await violations.DisposeAsync();
        }
    }

    /// <summary>
    /// The browser's cache folder, passed to the network session explicitly beside its data folder (the profile):
    /// <c>cache/WebKit</c> under the data root, named after the engine as the profile is.
    /// </summary>
    internal static string CacheFolder(SparFolders folders) => Path.Combine(folders.Cache, "WebKit");

    /// <summary>
    /// Once the main loop has ended and nothing can call back: the handles native callbacks found this window, its view
    /// and its scheme by.
    /// </summary>
    public void Release()
    {
        _view?.Release();
        _pages?.Release();
        _theme?.Release();
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        _stopping.Dispose();
    }

    /// <summary>Once the services exist: the backend's close guard.</summary>
    private async Task ConnectAsync(Task<SparRunningApp> built)
    {
        try
        {
            var running = await built;
            if (_opened is null && _windows is not null)
            {
                // The app's C# code reaches its windows through IWindows from here on.
                running.Services.GetService<SparWindows>()?.Use(_windows);
            }

            _guard = new CloseGuard(_app.Builder.CloseGuard, running.Services, _log.Logger);
            _hangHook.Start(running.Services);
        }
        catch (Exception exception)
        {
            _log.Error("The app could not start", exception);
            Fail(StartFailedExitCode, "The app could not start: " + exception.Message);
            return;
        }

        if (_state == State.Starting)
        {
            _state = State.Running;
        }
    }

    /// <summary>
    /// An unrecoverable failure: a message owned by the window, then the teardown with its exit code. In the automated
    /// mode nobody can answer, so the message goes to the log only. Safe from any thread.
    /// </summary>
    /// <summary>
    /// The window's grants and dialogs (made on the bridge's reader once the services exist): GTK's file dialog owned
    /// by this window, or the test host's fake when the app runs under it (it registers one as a service).
    /// </summary>
    private HostWindow HostWindow(MainLoop ui, SparRunningApp app)
    {
        var fake = app.Services.GetService<INativeFileDialogs>();
        if (fake is null)
        {
            _fileDialogs = new FileDialogHost(ui, () => _window, _log);
        }

        var profile = app.Services.GetService<BrowserProfile>();
        return new HostWindow(app.Dispatcher.Time, fake ?? _fileDialogs, profile is null ? null : profile.Contains)
        {
            Name = Name,
            Id = Id,
            Params = _opened?.Params ?? ReadOnlyMemory<byte>.Empty,
            Access = Options.Access,
            Windows = _windows,
            Handle = (IAppWindow?)_opened?.Handle ?? _windows?.Main,
            ZoomEnabled = Options.ZoomLimits.Enabled,
            Zoom = ZoomNow,
        };
    }

    private void Fail(int exitCode, string message) => _ui?.Post(() => _ = FailAsync(exitCode, message), "failure");

    private async Task FailAsync(int exitCode, string message)
    {
        if (_state == State.Closing || _failed)
        {
            return;
        }

        _failed = true;
        _exitCode = exitCode;
        _log.Error($"Exiting with code {exitCode}: {message}");
        _view?.EndRecovery();
        if (!_automated)
        {
            await AlertDialog.ChooseAsync(_window, _dialogs, $"{_title} has to close", message, "Close");
        }

        await TeardownAsync(exitCode);
    }

    /// <summary>
    /// The page's <c>setColorScheme</c> (any thread; the bridge's host command saved it already): the window takes it
    /// on the loop, as it takes a change of the system's theme, and the task completes once it has. For the system's
    /// scheme without a portal followed since the start, the portal is read here first, off the loop.
    /// </summary>
    private async Task ChooseSchemeAsync(bool? dark)
    {
        if (_ui is not { } ui || _theme is not { } theme)
        {
            return;
        }

        var portal = dark is null && !theme.HasPortal ? await Task.Run(WindowTheme.ReadPortalScheme) : default;
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(
            () =>
            {
                try
                {
                    ApplyScheme(theme, dark, portal);
                }
                finally
                {
                    applied.TrySetResult();
                }
            },
            "colour scheme");
        await applied.Task;
    }

    /// <summary>The window's layers and the view's background in the chosen scheme (loop thread).</summary>
    private void ApplyScheme(WindowTheme theme, bool? dark, (nint Bus, bool? Dark) portal)
    {
        theme.AdoptPortal(portal.Bus, portal.Dark);
        if (_state == State.Closing || _window == 0)
        {
            return;
        }

        if (theme.Choose(dark))
        {
            _view?.SetBackground(theme.Background);
        }

        _log.Info($"Colour scheme chosen by the page: {SavedColorScheme.NameOf(dark)}, the window {(theme.IsDark ? "dark" : "light")}.");
    }

    /// <summary>The policy the page gets, in the log once it is composed (off the loop).</summary>
    private async Task LogPolicyAsync(SchemeServer pages)
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

    /// <summary>
    /// After the window is destroyed: the main loop keeps running until the WebKit processes have exited (plan section
    /// 3, "Threading"), polled off the loop, for at most <see cref="WebProcessExitMilliseconds"/>. A web process still
    /// there then has no window left to serve: WebKit keeps the one it started for a navigation that never committed
    /// (the window sent to an address declared for frames, which the lock stopped), and the app then took about 10 s
    /// more to end, with SIGABRT (M1c step 4). Such web processes are ended, and the network process, which holds the
    /// stored data, is waited for again; it is never ended here.
    /// </summary>
    private async Task WaitForWebProcessesAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var left = await WaitForExitAsync(started);
        var pages = left.Where(process => process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal)).ToList();
        if (pages.Count > 0)
        {
            foreach (var (id, _) in pages)
            {
                _ = Libc.Kill(id, Libc.SigKill);
            }

            _log.Warn($"Web processes still running after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms with no window left: {Names(pages)}; ended.");
            left = await WaitForExitAsync(Stopwatch.GetTimestamp());
        }

        var waited = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (left.Count == 0)
        {
            _log.Step($"web processes exited after {waited:0} ms");
        }
        else
        {
            _log.Warn($"WebKit processes still running after {waited:0} ms: {Names(left)}.");
        }

        static string Names(List<(int Id, string Name)> processes) => string.Join(", ", processes.Select(p => $"{p.Name} {p.Id}"));
    }

    /// <summary>The app's WebKit processes still there when they have all exited or the wait is over.</summary>
    private async Task<List<(int Id, string Name)>> WaitForExitAsync(long started)
    {
        while (true)
        {
            var left = await Task.Run(_owner.WebKitProcesses);
            if (left.Count == 0 || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= WebProcessExitMilliseconds)
            {
                return left;
            }

            await Task.Delay(WebProcessPollMilliseconds);
        }
    }

    /// <summary>The heartbeat's interval: 5 s, or a Development build's override (the call is trimmed from Production).</summary>
    private static int HeartbeatInterval() =>
        SparDevelopment.IsSupported ? DevelopmentTriggers.HeartbeatInterval() : PageHeartbeat.IntervalMilliseconds;

    [UnmanagedCallersOnly]
    private static int OnCloseRequest(nint window, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not AppWindow owner)
        {
            return 0;
        }

        using var scope = owner._ui!.Enter("close-request");
        try
        {
            owner.OnCloseRequested();
        }
        catch (Exception exception)
        {
            owner.Report(exception);
        }

        // The window is never closed by GTK: only an approved close request's teardown destroys it.
        return 1;
    }

    private enum State
    {
        Starting,
        Running,
        Closing,
    }
}
