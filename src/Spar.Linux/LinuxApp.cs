using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spar.AppWindows;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Logging;
using Spar.Runtime;

namespace Spar.Linux;

/// <summary>
/// One run of the app on Linux (plan section 3, "Linux"). Before anything else, the start-up of plan section 3 on the
/// process's main thread: a display (exit 6), single instance (a later launch hands its arguments to the running one
/// and exits), the browser's environment overrides removed (plan section 3c), the libraries, WebKitGTK's version, its
/// helpers and a trial sandbox run (3, 4, 5), then, before GTK or any other native thread starts, the engine's
/// WebAssembly setting and the software-GPU default in the environment, and the theme. Then the app's services, built on the thread pool while the main window (<see cref="AppWindow"/>)
/// shows and starts its view on the main-loop thread; once the window is gone and its web processes have exited, the
/// services' disposal, with the main loop still running so the shutdown hang policy's prompt can show
/// (<see cref="ShutdownWatch"/>), and the exit code.
/// </summary>
internal sealed partial class LinuxApp : IAppWindowOwner, IAppWindowHost
{
    /// <summary>How long the process waits at exit for a relaunch handler that is still running.</summary>
    private const int RelaunchSettleMilliseconds = 3_000;

    /// <summary>How long the process waits at exit for the page hang hook's changes already reported.</summary>
    private const int HookSettleMilliseconds = 3_000;

    /// <summary><c>sysconf(_SC_CLK_TCK)</c>, 100 on every Linux the host runs on.</summary>
    private const double TicksPerSecond = 100;

    private readonly SparApp _app;
    private readonly HostLog _log = new();
    private readonly bool _automated = SparDevelopment.IsAutomatedFromEnvironment();
    private readonly string _title;
    private readonly string _identity;
    private readonly SparFolders _folders;
    private readonly RelaunchQueue _relaunches;
    private readonly PageHangHook _hangHook;

    /// <summary>The browser data folders no file grant may reach, with the one in use once the network session exists.</summary>
    private readonly BrowserProfile _profile;

    /// <summary>A Development build's scripted answers to the prompts; null in Production, where the person answers.</summary>
    private readonly ConcurrentQueue<string>? _promptAnswers;
    private LogLevels? _levels;
    private SingleInstance? _instance;
    private SparRunningApp? _running;
    private MainLoop? _ui;
    private AppWindow? _window;
    private AppWindowSet? _windowSet;
    private Task<SparRunningApp>? _built;
    private SparPageSecurity? _security;
    private ShutdownWatch? _shutdown;
    private SessionEndSignals? _signals;
    private DevServer.DevServerSession? _devServer;
    private Spar.Devtools.DevtoolsHost? _devtools;

    /// <summary>The running session end, once a signal started one: its final line and the log's flush.</summary>
    private Task? _sessionEnd;
    private int _sessionEnding;
    private int _stopped;

    /// <summary>Completes once <see cref="Stop"/> has run: the run's last work, which a session end waits for within its budget.</summary>
    private readonly TaskCompletionSource _stoppedWork = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The display GTK could not open, when that is why the window never started.</summary>
    private StartupProblem? _noDisplay;

    /// <summary>What a start-up report needs from this run.</summary>
    private StartupContext Startup => new(_title, _identity, _app.Builder.StartupProblemHandler, _log, _automated);

    public LinuxApp(SparApp app)
    {
        _app = app;
        _title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
        _identity = SparFolders.ChannelId(app.AppId, SparDevelopment.IsSupported);
        _folders = app.Builder.ResolveFolders();
        _relaunches = new RelaunchQueue(app.Builder.RelaunchHandler, _log.Logger);
        _hangHook = new PageHangHook(app.Builder.PageHangHandler, _log.Logger);
        _profile = new BrowserProfile(_folders.Profile) { InMemory = _folders.BrowserDataInMemory };
        _promptAnswers = SparDevelopment.IsSupported ? DevelopmentTriggers.PromptAnswers() : null;
    }

    public int Run()
    {
        // An installation file that cannot be used stops the start before anything else, with its key named.
        if (_app.Builder.InstallationProblem is { } installation)
        {
            var refused = StartupFailure.ReportConfiguration(installation, AppWindow.StartFailedExitCode, Startup);
            _log.Close();
            return refused;
        }

        // The levels first, so the host's first line is filtered as the app asked; logging.json is a small local file.
        var logging = _app.Builder.LoggingOptions;
        _levels = SparLogging.Levels(logging, _folders.DataRoot, SparDevelopment.IsSupported, out var problem);
        if (logging.WritesFile)
        {
            _log.UseFile(_folders.Logs, _levels);
        }

        _log.Info($"Host started {ProcessAgeMilliseconds():0} ms after the process; {BuildIdentity.Describe(_app.Catalog.BuildIdentity.Span)}.");
        if (problem is not null)
        {
            _log.Warn(problem);
        }

        try
        {
            _log.Info($"App id {_identity}, folder {_folders.DataRoot}");
            if (_app.Builder.Installation is { } installed)
            {
                _log.Info($"Installation file {installed.Path} sets {installed.Describe()}.");
            }

            if (_app.Builder.BrowserOptions.Active() is { Length: > 0 } active)
            {
                _log.Warn($"Browser opt-ins in force: {active}");
            }

            return CheckThenRun();
        }
        finally
        {
            // A session end's final line comes before the process ends, even when the normal exit got here first
            // (measured in the CI container: the services were disposed and the exit logged before the line).
            WaitForSessionEnd();

            // The folders the app removes at exit go while this instance still holds the lock (the window's teardown
            // waited for the web processes), the logs after the log closes.
            var removing = _instance is not null && _folders.RemovedOnExit.Count > 0;
            if (removing)
            {
                FolderRemoval.Remove(_folders, includeLogs: false, _log.Warn);
            }

            _log.Close();
            if (removing && FolderRemoval.RemovesLogs(_folders))
            {
                FolderRemoval.Remove(_folders with { RemovedOnExit = [SparFolder.Logs] }, includeLogs: true, _ => { });
            }

            // The socket closes before the lock is released, so no launch is acknowledged by a closing app.
            _signals?.Dispose();
            _instance?.Dispose();
            _shutdown?.Complete();
        }
    }

    /// <summary>The teardown started: no more relaunches, and the shutdown is watched until the main loop ends.</summary>
    void IAppWindowOwner.TeardownStarted()
    {
        var ui = _ui!;
        _shutdown = ShutdownWatch.Start(
            _title,
            _log,
            _automated,
            ShutdownHang.FirstPromptMilliseconds,
            ShutdownHang.AskAgainMilliseconds,
            request => LinuxPageHost.ShowShutdownPrompt(ui, _log, _promptAnswers, request),
            ExitNow);
        _instance?.StopListening();
        CloseSecondaries();
    }

    /// <summary>
    /// The window is gone and its web processes have exited (loop thread): the rest of the shutdown runs off the loop,
    /// which keeps running meanwhile so the shutdown prompt can show, and ends once the shutdown is done, after the
    /// app's other windows are gone too.
    /// </summary>
    void IAppWindowOwner.WindowClosed()
    {
        _mainClosed = true;
        FinishWhenDone();
    }

    /// <summary>Every WebKit process below this one: an app has one window.</summary>
    List<(int Id, string Name)> IAppWindowOwner.WebKitProcesses() => WebProcesses.Below(Environment.ProcessId);

    /// <summary>
    /// Plan section 3's Linux order: the display, then single instance (before anything touches the browser's folders,
    /// and before the slower checks, so a relaunch hands over at once), then the browser's environment overrides, the
    /// libraries and the sandbox, then the page security and the environment GTK and WebKit read at their start, and
    /// the theme.
    /// </summary>
    private int CheckThenRun()
    {
        if (Prerequisites.CheckDisplay(Environment.GetEnvironmentVariable) is { } noDisplay)
        {
            return StartupFailure.Report(noDisplay, Startup, gtkUsable: false);
        }

        var builder = _app.Builder;
        var token = ActivationToken();
        var claim = SingleInstance.Claim(_folders.DataRoot, new Relaunch(builder.Args, Environment.CurrentDirectory), token, _log);
        if (claim.Problem is { } unreachable)
        {
            return StartupFailure.Report(unreachable, Startup, Prerequisites.CanShowGtkMessage());
        }

        if (claim.Instance is null)
        {
            _log.Step("exit 0 (handed to the running instance)");
            return 0;
        }

        _instance = claim.Instance;
        _log.Phase("single instance");

        // What a crash or a killed session left of the folders the app removes at exit (this run's log stays).
        if (_folders.RemovedOnExit.Count > 0)
        {
            FolderRemoval.Remove(_folders, includeLogs: false, _log.Warn);
        }

        if (CreateProfileFolder() is { } unusable)
        {
            return StartupFailure.ReportConfiguration(unusable, AppWindow.StartFailedExitCode, Startup);
        }

        // The browser's environment overrides before any of WebKit's libraries is loaded (plan section 3c): what is
        // removed here never reaches this process's WebKit, JavaScriptCore or the web processes.
        if (EngineEnvironment.Filter(builder.BrowserOptions.EnvironmentOverrides, SparDevelopment.IsSupported, _log) is { } kept)
        {
            _log.Error(kept);
            return AppWindow.StartFailedExitCode;
        }

        var distribution = Distribution.Read();
        if (Prerequisites.CheckLibraries(distribution, _log, out var gtkUsable) is { } missing)
        {
            return StartupFailure.Report(missing, Startup, gtkUsable);
        }

        _log.Phase("prerequisites checked");

        // The page security the build read, now: the engine's WebAssembly setting is part of it, and the environment
        // has to carry it before WebKit starts.
        var security = PageSecuritySetup.Resolve(_app, _log.Logger);

        // The environment before any native thread exists: WebAssembly as the app declared it, the software-GPU default,
        // then (the session bus's first use starts GDBus's thread) the theme.
        if (EngineEnvironment.Apply(security.AllowWebAssembly, _log) is { } unset)
        {
            _log.Error(unset);
            return AppWindow.StartFailedExitCode;
        }

        GpuCheck.Apply(builder.BrowserOptions.Acceleration, _log);
        var theme = WindowTheme.Read(builder.MainWindow, _folders.DataRoot);
        _log.Phase("theme read");
        if (DevServer.DevServerSettings.IsSupported && StartDevServer() is { } noNode)
        {
            return StartupFailure.Report(noNode, Startup, gtkUsable);
        }

        if (Spar.Devtools.SparDevtools.IsSupported)
        {
            // Before the services are built on the pool: the merged log is one of the app's logger providers.
            _devtools = Spar.Devtools.DevtoolsHost.Prepare(builder.Services, "linux");
        }

        var built = Task.Run(Build);
        _built = built;
        _security = security;
        _windowSet = new AppWindowSet(builder, this, message => _log.Warn(message))
        {
            Placements = PlacementStore.Open(_folders.DataRoot, message => _log.Warn(message)),
        };
        _window = new AppWindow(
            _app, security, _log, _automated, _title, _folders, _profile, _hangHook, _promptAnswers, this, windows: _windowSet)
        {
            Family = _family,
        };
        _family.Add(_window);
        if (DevServer.DevServerSettings.IsSupported && _devServer is not null)
        {
            _window.UseDevServer(_devServer);
        }

        if (Spar.Devtools.SparDevtools.IsSupported && _devtools is { } devtools)
        {
            _window.UseDevtools(devtools);
            devtools.EndSession = EndSessionFromDevtools;
        }
        var started = false;
        MainLoop.Run(_window.Report, ui => started = Start(ui, theme, built));

        // Done already when the window ran; the handles native callbacks found it by go once nothing can call back.
        Stop();
        _window.Release();
        ReleaseSecondaries();
        if (_noDisplay is { } closed)
        {
            return StartupFailure.Report(closed, Startup, gtkUsable: false);
        }

        var exitCode = started ? _window.ExitCode : AppWindow.StartFailedExitCode;
        _log.Step($"exit {exitCode}");
        return exitCode;
    }

    /// <summary>
    /// On the loop thread: the program's name (the window's class and app id), GTK, the stall probe, the window, then
    /// the relaunch socket. False when GTK cannot open the display after all (exit 6) or the window failed.
    /// </summary>
    private bool Start(MainLoop ui, WindowTheme theme, Task<SparRunningApp> built)
    {
        _ui = ui;
        GLib.g_set_prgname(_identity);
        if (Gtk.gtk_init_check() == 0)
        {
            var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 } wayland
                ? wayland
                : Environment.GetEnvironmentVariable("DISPLAY");
            _noDisplay = Prerequisites.DisplayNotOpened(display);
            theme.Release();
            return false;
        }

        _log.Phase("gtk ready");
        if (_app.Builder.DiagnosticsOptions.CreateStallProbe(_log.Logger) is { } probe)
        {
            ui.UseProbe(probe);
            _log.Info("UI-thread stall probe on.");
        }

        try
        {
            using var scope = ui.Enter("window start");
            _window!.Start(ui, theme, built);
        }
        catch (Exception exception)
        {
            // GTK, WebKit or the window failed before the loop ran: nothing is shown, and the app cannot start.
            _log.Error("The window could not be created", exception);
            return false;
        }

        _instance?.Listen(OnRelaunch, _log);

        // From here on a session end has a window to close; before it, the runtime's default ends the process.
        _signals = SessionEndSignals.Register(OnSessionEnd, OnSignalAgain);
        return true;
    }

    /// <summary>
    /// The first <c>SIGTERM</c> or <c>SIGINT</c> (the runtime's signal thread): the session end, off that thread and off the
    /// loop. Its final line is written and the log flushed within the budget; when the last work is still running then,
    /// the process ends as Windows ends it after <c>WM_ENDSESSION</c>, with the exit code the session end's close gives (0).
    /// </summary>
    private void OnSessionEnd(string signal)
    {
        if (Interlocked.Exchange(ref _sessionEnding, 1) != 0)
        {
            // The devtools started it first: the one under way ends the run.
            _log.Step($"{signal}: the session is already ending");
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _log.Step($"{signal}: the session ends");
        var ui = _ui!;
        var window = _window!;
        _sessionEnd = Task.Run(async () =>
        {
            var outcome = await SessionEnd.RunAsync(ui, window, () => _stoppedWork.Task, start);
            _log.Step($"session end: {outcome.Text}; {signal} held {outcome.HeldMilliseconds:0} ms");
            _log.Close(SessionEnd.Left(start) > TimeSpan.FromMilliseconds(250) ? SessionEnd.Left(start) : TimeSpan.FromMilliseconds(250));
            if (!outcome.Finished)
            {
                ExitNow(window.ExitCode);
            }
        });
    }

    /// <summary>
    /// The devtools' <c>stop</c>, for <c>spar dev</c>'s restarts (decision "M2b's decide-first questions", item 3): the
    /// session end <c>SIGTERM</c> starts, off the devtools' thread; false until a session end has a window to close, or
    /// once one is under way.
    /// </summary>
    private bool EndSessionFromDevtools()
    {
        if (_signals is null || Volatile.Read(ref _sessionEnding) != 0)
        {
            return false;
        }

        _ = Task.Run(() => OnSessionEnd("devtools stop"));
        return true;
    }

    /// <summary>Waits briefly for a session end that a signal started: everything it waits for is done by now.</summary>
    private void WaitForSessionEnd()
    {
        try
        {
            _ = _sessionEnd?.Wait(SessionEnd.Budget);
        }
        catch (AggregateException exception)
        {
            _log.Error("The session end failed", exception.InnerException);
        }
    }

    /// <summary>A second signal: the process ends at once, with 128 + the signal's number, after one line.</summary>
    private void OnSignalAgain(string signal, int exitCode)
    {
        _log.Step($"{signal} again: exit {exitCode} at once");
        _log.Close(TimeSpan.FromMilliseconds(250));
        ExitNow(exitCode);
    }

    /// <summary>
    /// A later launch's arguments, on the socket's thread (decision "The relaunch handler"): the window is presented
    /// on the loop thread, then the relaunch joins the handler's queue. True acknowledges it; a closing app refuses it.
    /// </summary>
    private bool OnRelaunch(Relaunch relaunch, string? activationToken)
    {
        var ui = _ui;
        var window = _window;
        if (ui is null || window is null || window.IsClosing)
        {
            return false;
        }

        ui.Post(
            () =>
            {
                if (window.IsClosing)
                {
                    return;
                }

                window.BringForward(relaunch.Arguments.Count, activationToken);
                _relaunches.TryAdd(relaunch);
            },
            "relaunch");
        return true;
    }

    /// <summary>
    /// The launcher's token that lets this launch's window take the focus: <c>XDG_ACTIVATION_TOKEN</c> on Wayland,
    /// <c>DESKTOP_STARTUP_ID</c> on X11. A relaunch hands it to the running instance's window.
    /// </summary>
    private static string? ActivationToken() =>
        Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN") is { Length: > 0 } wayland
            ? wayland
            : Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID") is { Length: > 0 } x11 ? x11 : null;

    /// <summary>
    /// Creates the browser data folder before the network session exists: a folder WebKit cannot create fails late and
    /// unclearly, and it is the app's configuration (exit 70), named in the message.
    /// </summary>
    private string? CreateProfileFolder()
    {
        // An absolute path on a local drive, as on Windows (spar.Folders(f => f.Profile(…))).
        if (SparFolders.ProfileProblem(_app.Builder.FolderOptions.ProfileFolder) is { } problem)
        {
            return problem;
        }

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

    /// <summary>Builds the app's services on the thread pool while the window and the view start.</summary>
    private SparRunningApp Build()
    {
        var builder = _app.Builder;

        // The browser data folders no file grant may reach, with the folder in use once the network session exists.
        builder.Services.AddSingleton(_profile);

        // The app's loggers write to the file the host opened at its start; the host closes it at process exit.
        Func<LogFile>? file = _log.File is { } opened ? () => opened : null;
        // The OS services the app may inject, before Spar's own fallbacks; an app's own registration wins over both.
        builder.Services.TryAddSingleton<IClipboard>(new LinuxClipboard(() => _ui));
        builder.Services.TryAddSingleton<ICredentialStore>(new LinuxCredentials(builder.AppId));
        SparLogging.Add(builder.Services, _levels!, file, ownsFile: false);
        SparRunningApp.AddSparServices(builder.Services, _app.Catalog, _folders, builder.BridgeOptions.Freeze());
        if (SparDevelopment.IsSupported)
        {
            DevelopmentTriggers.AddSlowDisposal(builder.Services, _log);
        }

        var running = SparRunningApp.Build(builder.Services, _app.Catalog, _folders);
        if (SparDevelopment.IsSupported)
        {
            DevelopmentTriggers.CreateSlowDisposal(running.Services);
        }

        _log.UseAppLogging(running.Services);
        _log.Phase("services built");
        _running = running;
        if (Spar.Devtools.SparDevtools.IsSupported)
        {
            _devtools?.Start(running, _log.RunId, _log.Logger);
        }

        _relaunches.Start(running.Services);
        return running;
    }

    /// <summary>On the loop, once the window is gone: the shutdown off the loop, then the watch completed and the loop ended.</summary>
    private async Task FinishAsync(MainLoop ui)
    {
        await Task.Run(Stop);
        _shutdown?.Complete();
        ui.Quit();
    }

    /// <summary>
    /// After the window is gone, off the main loop (which still runs, or has ended when the window never started):
    /// relaunches, the hang hook, the window's bridge, then the app's services.
    /// </summary>
    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            // This is a pool thread or the process's main thread, never the loop's, so waiting here blocks nothing.
            if (!_relaunches.StopAsync().Wait(RelaunchSettleMilliseconds))
            {
                _log.Warn("A relaunch handler was still running at exit.");
            }

            if (!_hangHook.StopAsync(TimeSpan.FromMilliseconds(HookSettleMilliseconds)).GetAwaiter().GetResult())
            {
                _log.Warn("The page hang hook was still running at exit: its token was cancelled.");
            }

            _window?.StopAsync().GetAwaiter().GetResult();
            if (Spar.Devtools.SparDevtools.IsSupported)
            {
                _devtools?.StopAsync().AsTask().GetAwaiter().GetResult();
            }

            // The development server's output stops before the services, whose logging it writes through.
            if (DevServer.DevServerSettings.IsSupported)
            {
                _devServer?.Dispose();
            }

            _running?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _log.Step("services disposed");
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly", exception);
        }
        finally
        {
            _stoppedWork.TrySetResult();
        }
    }

    /// <summary>
    /// A Debug build's development server (plan section 4): found, reused or started beside the services and the window.
    /// Starting one needs Node and npm, which are checked first; without them the app stops with exit code 3.
    /// </summary>
    private StartupProblem? StartDevServer()
    {
        if (DevServer.DevServerSettings.Read() is not { } settings)
        {
            return null;
        }

        if (settings.Url is null && DevServer.DevServerLauncher.CheckNode() is { } noNode)
        {
            return noNode;
        }

        _devServer = DevServer.DevServerSession.Start(settings, _log);
        return null;
    }

    /// <summary>
    /// "Exit now" (the shutdown hang policy): the user chose to end the app, so its WebKit processes that are still
    /// there go with it, then this process ends at once with <paramref name="exitCode"/>, skipping every cleanup still
    /// running or waiting, exit handlers included.
    /// </summary>
    private static void ExitNow(int exitCode)
    {
        foreach (var (id, _) in WebProcesses.Below(Environment.ProcessId))
        {
            _ = Libc.Kill(id, Libc.SigKill);
        }

        Libc.ExitNow(exitCode);
    }

    /// <summary>
    /// How long the process ran before the host started: its start time in <c>/proc/self/stat</c> (field 22, in clock
    /// ticks since boot, 10 ms each on Linux) against <c>CLOCK_BOOTTIME</c>. NaN when it cannot be read.
    /// </summary>
    private static unsafe double ProcessAgeMilliseconds()
    {
        try
        {
            var stat = File.ReadAllText("/proc/self/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            var startTicks = long.Parse(fields[19], System.Globalization.CultureInfo.InvariantCulture);
            Libc.Timespec now;
            if (Libc.ClockGetTime(Libc.ClockBootTime, &now) != 0)
            {
                return double.NaN;
            }

            var nowMilliseconds = (now.Seconds * 1_000.0) + (now.Nanoseconds / 1_000_000.0);
            return nowMilliseconds - (startTicks * 1_000.0 / TicksPerSecond);
        }
        catch (Exception exception) when (exception is IOException or FormatException or IndexOutOfRangeException)
        {
            return double.NaN;
        }
    }
}

/// <summary>
/// The Linux host as Spar's generated code creates it (<see cref="ISparHost"/>): one window, its WebKitGTK view and the
/// app's backend. Apps never reference it; Spar.Sdk adds it for <c>SparPlatforms</c> that include linux.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class SparLinuxHost : ISparHost, ISparTestWindowHost
{
    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new LinuxApp(app).Run();
    }

    /// <summary>The app's window in a test process (the Window surface), behind the test-host feature switch.</summary>
    ISparTestWindow ISparTestWindowHost.Open(SparApp app, SparRunningApp running, SparTestWindowRequest request) =>
        Testing.TestWindow.Open(app, running, request);
}
