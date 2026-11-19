using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Logging;
using Spar.Runtime;

namespace Spar.Linux;

/// <summary>
/// One run of the app on Linux (plan section 3, "Linux"): the app's services, built on the thread pool while the main
/// window (<see cref="AppWindow"/>) shows and starts its view on the main-loop thread; then, once the window is gone
/// and its web processes have exited, the services' disposal and the exit code. The start-up checks with their exit
/// codes, single instance and the theme layers come in M1c step 2, the shutdown watch in step 3: this run only refuses
/// to start without a display (exit 6), which GTK reports.
/// </summary>
internal sealed partial class LinuxApp : IAppWindowOwner
{
    /// <summary>Plan section 3, "Missing prerequisites": no graphical session.</summary>
    private const int NoDisplayExitCode = 6;

    /// <summary><c>CLOCK_BOOTTIME</c>, the clock of the process start times in <c>/proc</c>.</summary>
    private const int BootTime = 7;

    /// <summary><c>sysconf(_SC_CLK_TCK)</c>, 100 on every Linux the host runs on.</summary>
    private const double TicksPerSecond = 100;

    private readonly SparApp _app;
    private readonly HostLog _log = new();
    private readonly bool _automated = SparDevelopment.IsAutomatedFromEnvironment();
    private readonly string _title;
    private readonly SparFolders _folders;
    private LogLevels? _levels;
    private SparRunningApp? _running;
    private MainLoop? _ui;
    private AppWindow? _window;
    private int _stopped;

    /// <summary>The exit code when the window never started: no display, unless the start itself failed.</summary>
    private int _notStarted = NoDisplayExitCode;

    public LinuxApp(SparApp app)
    {
        _app = app;
        _title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
        _folders = app.Builder.ResolveFolders();
    }

    public int Run()
    {
        // The levels first, so the host's first line is filtered as the app asked; logging.json is a small local file.
        var logging = _app.Builder.LoggingOptions;
        _levels = SparLogging.Levels(logging, _folders.DataRoot, SparDevelopment.IsSupported, out var problem);
        if (logging.WritesFile)
        {
            _log.UseFile(_folders.Logs, _levels);
        }

        _log.Info($"Host started {ProcessAgeMilliseconds():0} ms after the process.");
        if (problem is not null)
        {
            _log.Warn(problem);
        }

        try
        {
            var identity = SparFolders.ChannelId(_app.AppId, SparDevelopment.IsSupported);
            _log.Info($"App id {identity}, folder {_folders.DataRoot}");
            if (_app.Builder.BrowserOptions.Active() is { Length: > 0 } active)
            {
                _log.Warn($"Browser opt-ins in force: {active}");
            }

            if (CreateProfileFolder() is { } unusable)
            {
                Console.Error.WriteLine("spar fail: " + unusable);
                _log.Error(unusable);
                return AppWindow.StartFailedExitCode;
            }

            var built = Task.Run(Build);
            _window = new AppWindow(_app, _log, _automated, _title, _folders, this);
            var started = false;
            MainLoop.Run(_window.Report, ui => started = Start(ui, identity, built));

            Stop();
            var exitCode = started ? _window.ExitCode : _notStarted;
            _log.Step($"exit {exitCode}");
            return exitCode;
        }
        finally
        {
            _log.Close();
        }
    }

    void IAppWindowOwner.TeardownStarted()
    {
    }

    void IAppWindowOwner.WindowClosed() => _ui?.Quit();

    /// <summary>
    /// On the loop thread: the program's name (the window's class and app id), GTK, the stall probe, then the window.
    /// False when GTK finds no display.
    /// </summary>
    private bool Start(MainLoop ui, string identity, Task<SparRunningApp> built)
    {
        _ui = ui;
        GLib.g_set_prgname(identity);
        if (Gtk.gtk_init_check() == 0)
        {
            var message = "No graphical session: GTK could not open a display (DISPLAY or WAYLAND_DISPLAY).";
            Console.Error.WriteLine("spar fail: " + message);
            _log.Error(message);
            return false;
        }

        _log.Phase("gtk ready");
        var version = $"{WebKit.webkit_get_major_version()}.{WebKit.webkit_get_minor_version()}.{WebKit.webkit_get_micro_version()}";
        _log.Info($"WebKitGTK {version}.");
        if (_app.Builder.DiagnosticsOptions.CreateStallProbe(_log.Logger) is { } probe)
        {
            ui.UseProbe(probe);
            _log.Info("UI-thread stall probe on.");
        }

        try
        {
            using var scope = ui.Enter("window start");
            _window!.Start(ui, built);
            return true;
        }
        catch (Exception exception)
        {
            // GTK, WebKit or the window failed before the loop ran: nothing is shown, and the app cannot start.
            Console.Error.WriteLine("spar fail: the window could not be created: " + exception.Message);
            _log.Error("The window could not be created", exception);
            _notStarted = AppWindow.StartFailedExitCode;
            return false;
        }
    }

    /// <summary>
    /// Creates the browser data folder before the network session exists: a folder WebKit cannot create fails late and
    /// unclearly, and it is the app's configuration (exit 70), named in the message.
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
            return $"The browser data folder '{_folders.Profile}' cannot be created: {exception.Message}";
        }
    }

    /// <summary>Builds the app's services on the thread pool while the window and the view start.</summary>
    private SparRunningApp Build()
    {
        var builder = _app.Builder;

        // The app's loggers write to the file the host opened at its start; the host closes it at process exit.
        Func<LogFile>? file = _log.File is { } opened ? () => opened : null;
        SparLogging.Add(builder.Services, _levels!, file, ownsFile: false);
        SparRunningApp.AddSparServices(builder.Services, _app.Catalog, _folders, builder.BridgeOptions.Freeze());
        var running = SparRunningApp.Build(builder.Services, _app.Catalog, _folders);
        _log.UseAppLogging(running.Services);
        _log.Phase("services built");
        _running = running;
        return running;
    }

    /// <summary>After the window is gone and the loop has ended: the window's bridge, then the app's services.</summary>
    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            // The loop's thread has ended, so waiting here blocks nothing.
            _window?.StopAsync().GetAwaiter().GetResult();
            _running?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _log.Step("services disposed");
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly", exception);
        }
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
            Timespec now;
            if (ClockGetTime(BootTime, &now) != 0)
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


    [LibraryImport("libc", EntryPoint = "clock_gettime")]
    private static unsafe partial int ClockGetTime(int clock, Timespec* time);

    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }
}

/// <summary>
/// The Linux host as Spar's generated code creates it (<see cref="ISparHost"/>): one window, its WebKitGTK view and the
/// app's backend. Apps never reference it; Spar.Sdk adds it for <c>SparPlatforms</c> that include linux.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class SparLinuxHost : ISparHost
{
    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new LinuxApp(app).Run();
    }
}
