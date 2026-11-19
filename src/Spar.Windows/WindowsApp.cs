using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Logging;
using Spar.Runtime;
using Windows.Win32;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Spar.Windows;

/// <summary>
/// One run of the app on Windows. Before anything else, the start-up of plan section 3: the Windows check, single
/// instance (a later launch hands its arguments to the running one and exits), and the WebView2 Runtime check, each
/// failure reported with its fixed exit code. Then the app's services, built on the thread pool while the main window
/// (<see cref="AppWindow"/>) shows and starts its WebView on the UI thread. From the teardown on,
/// <see cref="ShutdownWatch"/> watches for a shutdown that does not finish.
/// </summary>
internal sealed class WindowsApp : IAppWindowOwner
{
    /// <summary>The longest AppUserModelID Windows takes.</summary>
    private const int MaxAppUserModelId = 128;

    /// <summary>How long the process waits at exit for a relaunch handler that is still running.</summary>
    private const int RelaunchSettleMilliseconds = 3_000;

    /// <summary>How long the process waits at exit for the page hang hook to see its last change.</summary>
    private const int HookSettleMilliseconds = 3_000;

    private readonly SparApp _app;
    private readonly HostLog _log = new();
    private readonly bool _automated = SparDevelopment.IsAutomatedFromEnvironment();
    private readonly string _title;
    private readonly SparFolders _folders;
    private readonly BrowserProfile _profile;
    private readonly string _identity;
    private readonly RelaunchQueue _relaunches;
    private readonly PageHangHook _hangHook;
    private LogLevels? _levels;
    private SingleInstance? _instance;
    private SparRunningApp? _running;
    private UiThread? _ui;
    private AppWindow? _window;
    private ShutdownWatch? _shutdown;
    private int _stopped;

    /// <summary>What a start-up report needs from this run.</summary>
    private StartupContext Startup => new(_title, _app.Builder.StartupProblemHandler, _log, _automated);

    public WindowsApp(SparApp app)
    {
        _app = app;
        _title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
        _identity = SparFolders.ChannelId(app.AppId, SparDevelopment.IsSupported);
        _folders = app.Builder.ResolveFolders();
        _profile = new BrowserProfile(_folders.Profile);
        _relaunches = new RelaunchQueue(app.Builder.RelaunchHandler, _log.Logger);
        _hangHook = new PageHangHook(app.Builder.PageHangHandler, _log.Logger);
    }

    public int Run()
    {
        // The levels first, so the host's first line is filtered as the app asked; logging.json is a small local file.
        // Without a file (spar.Logging(l => l.WithoutFile())), the host's early lines go to standard error only.
        // A data root on a network drive is refused before the log file or logging.json is touched there.
        if (SparFolders.DataRootProblem(_folders.DataRoot) is { } networkRoot)
        {
            var refused = StartupFailure.ReportConfiguration(networkRoot, AppWindow.StartFailedExitCode, Startup);
            _log.Close();
            return refused;
        }

        var logging = _app.Builder.LoggingOptions;
        _levels = SparLogging.Levels(logging, _folders.DataRoot, SparDevelopment.IsSupported, out var problem);
        if (logging.WritesFile)
        {
            _log.UseFile(_folders.Logs, _levels);
        }

        _log.Info($"Host started {ProcessAgeMilliseconds():0.0} ms after the process.");
        if (problem is not null)
        {
            _log.Warn(problem);
        }

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

    /// <summary>The teardown started: no more relaunches, and the shutdown is watched until the process returns.</summary>
    void IAppWindowOwner.TeardownStarted()
    {
        _shutdown = ShutdownWatch.Start(_title, _log, _automated, ShutdownHang.FirstPromptMilliseconds, ShutdownHang.AskAgainMilliseconds);
        _instance?.StopListening();
    }

    void IAppWindowOwner.WindowDestroyed() => PInvoke.PostQuitMessage(0);

    /// <summary>At session end, the services are disposed while the window still holds <c>WM_ENDSESSION</c>.</summary>
    Task? IAppWindowOwner.SessionEnded() => Task.Run(Stop);

    /// <summary>The log file is closed before <c>WM_ENDSESSION</c> returns: queued lines are lost when Windows ends the process.</summary>
    void IAppWindowOwner.SessionEndLogged(TimeSpan limit) => _log.Close(limit);

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

        if (builder.BrowserOptions.Acceleration != HardwareAcceleration.Auto)
        {
            _log.Info($"HardwareAcceleration({builder.BrowserOptions.Acceleration}) is for Linux; Windows ignores it.");
        }

        BrowserEnvironment.Filter(builder.BrowserOptions.EnvironmentOverrides, _log);
        if (SparFolders.ProfileProblem(builder.FolderOptions.ProfileFolder) is { } problem)
        {
            return StartupFailure.ReportConfiguration(problem, AppWindow.StartFailedExitCode, Startup);
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

        if (CreateProfileFolder() is { } unusableProfile)
        {
            return StartupFailure.ReportConfiguration(unusableProfile, AppWindow.StartFailedExitCode, Startup);
        }

        if (Prerequisites.CheckRuntime() is { } runtime)
        {
            return StartupFailure.Report(runtime, Startup);
        }

        _log.Phase("prerequisites checked");
        var theme = WindowTheme.Read(builder.MainWindow, _folders.DataRoot);
        var built = Task.Run(() => Build(_folders));
        _window = new AppWindow(_app, _log, _automated, _title, _profile, _hangHook, this);

        UiThread.Run(theme.Background.ColorRef, _window.Report, ui => Start(ui, theme, built));

        Stop();
        var exitCode = _window.ExitCode;
        _log.Step($"exit {exitCode}");
        return exitCode;
    }

    /// <summary>
    /// Creates the browser profile's folder before the environment exists, once this process holds the single
    /// instance. WebView2 reports a folder it cannot create as a failed controller, which would read as a broken Runtime
    /// (exit 5, "install it again"); a folder problem is the app's configuration (exit 70) and names the folder.
    /// </summary>
    private string? CreateProfileFolder()
    {
        try
        {
            Directory.CreateDirectory(_folders.Profile);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"The browser profile folder '{_folders.Profile}' cannot be created: {exception.Message}";
        }
    }

    /// <summary>Builds the app's services on the thread pool while the window and the WebView start.</summary>
    private SparRunningApp Build(SparFolders folders)
    {
        var builder = _app.Builder;
        // The profile folders no file grant may reach, with the folder in use once the environment exists.
        builder.Services.AddSingleton(_profile);

        // The app's loggers write to the file the host opened at its start; the host closes it at process exit.
        Func<LogFile>? file = _log.File is { } opened ? () => opened : null;
        SparLogging.Add(builder.Services, _levels!, file, ownsFile: false);
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
        _running = running;
        _relaunches.Start(running.Services);
        return running;
    }

    /// <summary>On the UI thread: the stall probe, the window, then the relaunch pipe.</summary>
    private void Start(UiThread ui, WindowTheme theme, Task<SparRunningApp> built)
    {
        _ui = ui;
        if (_app.Builder.DiagnosticsOptions.CreateStallProbe(_log.Logger) is { } probe)
        {
            ui.UseProbe(probe);
            _log.Info("UI-thread stall probe on.");
        }

        _window!.Start(ui, theme, built);
        _instance?.Listen(OnRelaunch, _log);
    }

    /// <summary>
    /// A later launch's arguments, on the pipe's task (decision "The relaunch handler"): the window is shown and focused
    /// on the UI thread, then the relaunch joins the handler's queue. True acknowledges it; a closing app refuses it.
    /// </summary>
    private bool OnRelaunch(Relaunch relaunch)
    {
        var ui = _ui;
        var window = _window;
        if (ui is null || window is null || window.IsClosing)
        {
            return false;
        }

        ui.Post(() =>
        {
            if (window.IsClosing)
            {
                return;
            }

            window.BringForward(relaunch.Arguments.Count);
            _relaunches.TryAdd(relaunch);
        });
        return true;
    }

    /// <summary>
    /// After the window is gone: relaunches, the hang hook, the window's bridge and logs, then the app's services. Runs
    /// once, from the process's main thread once the window's thread has ended, or earlier at session end.
    /// </summary>
    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            // The window's thread has ended or holds WM_ENDSESSION while it pumps, so waiting here blocks nothing.
            if (!_relaunches.StopAsync().Wait(RelaunchSettleMilliseconds))
            {
                _log.Warn("A relaunch handler was still running at exit.");
            }

            if (!_hangHook.StopAsync(TimeSpan.FromMilliseconds(HookSettleMilliseconds)).GetAwaiter().GetResult())
            {
                _log.Warn("The page hang hook was still running at exit: its token was cancelled.");
            }

            _window?.StopAsync().GetAwaiter().GetResult();
            _running?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _log.Step("services disposed");
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly", exception);
        }
    }

    /// <summary>How long the process ran before the host started, from the process's creation time.</summary>
    private static unsafe double ProcessAgeMilliseconds()
    {
        FILETIME created, exited, kernel, user;
        if (!PInvoke.GetProcessTimes(PInvoke.GetCurrentProcess(), &created, &exited, &kernel, &user))
        {
            return double.NaN;
        }

        PInvoke.GetSystemTimePreciseAsFileTime(out var now);
        return (Ticks(now) - Ticks(created)) / (double)TimeSpan.TicksPerMillisecond;
    }

    private static long Ticks(FILETIME time) => ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
}

/// <summary>
/// The Windows host as Spar's generated code creates it (<see cref="ISparHost"/>): one window, its WebView and the
/// app's backend. Apps never reference it; Spar.Sdk adds it for <c>SparPlatforms</c> that include windows. In a test
/// process it also opens the app's window for the test host's <c>Window</c> surface (plan section 5, level 5).
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class SparWindowsHost : ISparHost, ISparTestWindowHost
{
    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new WindowsApp(app).Run();
    }

    ISparTestWindow ISparTestWindowHost.Open(SparApp app, SparRunningApp running, SparTestWindowRequest request) =>
        Testing.TestWindow.Open(app, running, request);
}
