using System.Runtime.InteropServices;

namespace Startup.Win;

internal enum ProblemKind
{
    Missing = 3,
    TooOld = 4,
    Broken = 5,
    NoDisplay = 6,
    OsTooOld = 7,
}

internal sealed record StartupProblem(ProblemKind Kind, string Component, string Summary, string Fix, string? Link, string Details)
{
    public int ExitCode => (int)Kind;
}

// The start-up check, in order: OS build, runtime presence, runtime version, environment, controller. Each step runs
// only if the previous one passed, and the first failure is returned.
internal sealed unsafe class Checks(Options o, Log log)
{
    // Microsoft's page for end users who need the Runtime (linked from "Distribute your app and the WebView2 Runtime").
    public const string DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/consumer/";
    private const string Runtime = "Microsoft Edge WebView2 Runtime";
    private const int ErrorFileNotFound = unchecked((int)0x80070002);

    private nint _environment;
    private nint _controller;
    private nint _window;
    private int _browserExits;

    public StartupProblem? Run()
    {
        var problem = CheckOs() ?? CheckRuntime(out var version) ?? CheckVersion(version!);
        if (problem is not null || o.Stage == Stage.Version)
        {
            return problem;
        }

        // The thread is an STA ([STAThread] on Main): WebView2 completions arrive through this thread's message pump.
        log.Mark("coinit", ("hr", Log.Hr(Native.CoInitializeEx(0, 0x2))));
        problem = CreateEnvironment();
        if (problem is null && o.Stage == Stage.Controller)
        {
            problem = CreateController();
        }

        Cleanup();
        return problem;
    }

    private StartupProblem? CheckOs()
    {
        Native.OsVersionInfoEx real = default;
        real.Size = (uint)sizeof(Native.OsVersionInfoEx);
        Native.RtlGetVersion(&real);
        Native.OsVersionInfo reported = default;
        reported.Size = (uint)sizeof(Native.OsVersionInfo);
        Native.GetVersionEx(&reported);
        log.Mark("os", ("real", $"{real.Major}.{real.Minor}.{real.Build}"), ("getVersionEx", $"{reported.Major}.{reported.Minor}.{reported.Build}"),
            ("minBuild", o.MinimumOsBuild));
        if (real.Major > 10 || (real.Major == 10 && real.Build >= o.MinimumOsBuild))
        {
            return null;
        }

        return new StartupProblem(ProblemKind.OsTooOld, "Windows",
            $"{o.AppName} needs Windows 10 build {o.MinimumOsBuild} or later. This PC runs Windows {real.Major}.{real.Minor} build {real.Build}.",
            "Update Windows, or run the app on a newer PC.", null,
            $"RtlGetVersion {real.Major}.{real.Minor}.{real.Build}; required build {o.MinimumOsBuild}");
    }

    private StartupProblem? CheckRuntime(out string? version)
    {
        var hr = WebView2.GetAvailableCoreWebView2BrowserVersionString(o.RuntimeFolder, out var info);
        version = info == 0 ? null : Marshal.PtrToStringUni(info);
        if (info != 0)
        {
            Native.CoTaskMemFree(info);
        }

        log.Mark("runtime-version", ("hr", Log.Hr(hr)), ("version", version), ("folder", o.RuntimeFolder));
        if (hr >= 0 && version is not null)
        {
            return null;
        }

        var where = o.RuntimeFolder is null ? "" : $" in {o.RuntimeFolder}";
        return hr is ErrorFileNotFound or >= 0
            ? new StartupProblem(ProblemKind.Missing, Runtime,
                $"{o.AppName} needs the {Runtime}, which is not installed on this PC.",
                "Install it from Microsoft's website, then start the app again.", DownloadPage,
                $"GetAvailableCoreWebView2BrowserVersionString{where}: {Log.Hr(hr)}, no version")
            : new StartupProblem(ProblemKind.Broken, Runtime,
                $"The {Runtime} on this PC could not be read (error {Log.Hr(hr)}).",
                "Reinstall it from Microsoft's website, then start the app again.", DownloadPage,
                $"GetAvailableCoreWebView2BrowserVersionString{where}: {Log.Hr(hr)}");
    }

    private StartupProblem? CheckVersion(string version)
    {
        var hr = WebView2.CompareBrowserVersions(version, o.MinimumRuntime, out var order);
        log.Mark("runtime-compare", ("hr", Log.Hr(hr)), ("found", version), ("minimum", o.MinimumRuntime), ("order", order));
        if (hr >= 0 && order >= 0)
        {
            return null;
        }

        // The version string can carry a channel suffix ("120.0.2210.55 beta"); CompareBrowserVersions handles it.
        return new StartupProblem(ProblemKind.TooOld, Runtime,
            $"{o.AppName} needs version {o.MinimumRuntime} or later of the {Runtime}. This PC has version {version}.",
            "Update it by running Microsoft's installer (or let Windows Update update it), then start the app again.", DownloadPage,
            $"CompareBrowserVersions({version}, {o.MinimumRuntime}) = {order}, {Log.Hr(hr)}");
    }

    private StartupProblem? CreateEnvironment()
    {
        int? result = null;
        var handler = WebView2.Wrap<IEnvironmentCompletedHandler>(new EnvironmentCompleted((hr, env) =>
        {
            result = hr;
            if (hr >= 0 && env != 0)
            {
                Marshal.AddRef(env);
                _environment = env;
            }
        }));
        var started = log.Now;
        var call = WebView2.CreateCoreWebView2EnvironmentWithOptions(o.RuntimeFolder, o.Profile, 0, handler);
        Marshal.Release(handler);
        log.Mark("environment-call", ("hr", Log.Hr(call)));
        if (call >= 0)
        {
            Pump(() => result is not null);
        }

        var hr = call < 0 ? call : result ?? unchecked((int)0x800705B4); // ERROR_TIMEOUT
        log.Mark("environment-done", ("hr", Log.Hr(hr)), ("ms", log.Now - started), ("timedOut", call >= 0 && result is null),
            ("version", _environment != 0 ? WebView2.BrowserVersion(_environment) : null));
        if (hr < 0 || _environment == 0)
        {
            return Broken("environment", hr);
        }

        Marshal.QueryInterface(_environment, WebView2.Environment5, out var env5);
        if (env5 != 0)
        {
            var exited = WebView2.Wrap<IBrowserProcessExitedHandler>(new BrowserProcessExited(args =>
            {
                var (kind, pid) = WebView2.ExitArgs(args);
                _browserExits++;
                log.Mark("browser-process-exited", ("kind", kind), ("pid", pid));
            }));
            WebView2.AddBrowserProcessExited(env5, exited, out _);
            Marshal.Release(exited);
            Marshal.Release(env5);
        }

        if (o.HoldMs > 0)
        {
            var until = log.Now + o.HoldMs;
            Pump(() => log.Now >= until, o.HoldMs);
        }

        BrowserChildren(kill: false);
        return null;
    }

    private StartupProblem? CreateController()
    {
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("the controller stage opens a window and runs only in Windows Sandbox");
        }

        _window = Window.Create(o.AppName);
        int? result = null;
        var handler = WebView2.Wrap<IControllerCompletedHandler>(new ControllerCompleted((hr, controller) =>
        {
            result = hr;
            if (hr >= 0 && controller != 0)
            {
                Marshal.AddRef(controller);
                _controller = controller;
            }
        }));
        var started = log.Now;
        var call = WebView2.CreateController(_environment, _window, handler);
        Marshal.Release(handler);
        if (call >= 0 && o.KillBrowser)
        {
            // The browser process starts with the first controller, not with the environment. A browser that crashes at
            // every start is simulated by ending each msedgewebview2.exe child as soon as it appears, until creation
            // completes or times out.
            while (result is null && log.Now - started < o.TimeoutMs)
            {
                BrowserChildren(kill: true);
                var slice = log.Now + 20;
                Pump(() => result is not null || log.Now >= slice, 20);
            }
        }

        if (call >= 0)
        {
            Pump(() => result is not null);
        }

        var hr = call < 0 ? call : result ?? unchecked((int)0x800705B4);
        log.Mark("controller-done", ("hr", Log.Hr(hr)), ("ms", log.Now - started), ("browserExits", _browserExits));
        return hr < 0 || _controller == 0 ? Broken("controller", hr) : null;
    }

    private StartupProblem Broken(string step, int hr) => new(ProblemKind.Broken, Runtime,
        $"The {Runtime} is installed, but it could not be started (error {Log.Hr(hr)}).",
        "Restart the PC and try again. If that does not help, reinstall the Runtime from Microsoft's website.", DownloadPage,
        $"{step} creation failed: {Log.Hr(hr)}; runtime folder {o.RuntimeFolder ?? "(installed runtime)"}; browser exits seen {_browserExits}");

    // Pumps this thread's messages until the condition holds or the timeout passes (a thread timer posts WM_TIMER).
    private void Pump(Func<bool> done, int? timeoutMs = null)
    {
        const uint WmTimer = 0x0113;
        var timer = Native.SetTimer(0, 0, (uint)(timeoutMs ?? o.TimeoutMs), 0);
        while (!done() && Native.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.Message == WmTimer && msg.Hwnd == 0 && (nuint)msg.WParam == timer)
            {
                break;
            }

            Native.TranslateMessage(msg);
            Native.DispatchMessage(msg);
        }

        Native.KillTimer(0, timer);
    }

    // Lists the msedgewebview2.exe children of this process; with kill, ends them (a browser that dies during start-up).
    private int BrowserChildren(bool kill)
    {
        var self = Native.GetCurrentProcessId();
        var snapshot = Native.CreateToolhelp32Snapshot(0x2, 0); // TH32CS_SNAPPROCESS
        Native.ProcessEntry entry = default;
        entry.Size = (uint)sizeof(Native.ProcessEntry);
        var killed = new List<uint>();
        for (var ok = Native.Process32First(snapshot, &entry); ok; ok = Native.Process32Next(snapshot, &entry))
        {
            var name = new string(entry.ExeFile);
            if (entry.ParentProcessId == self && name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
            {
                killed.Add(entry.ProcessId);
                if (kill)
                {
                    var handle = Native.OpenProcess(0x0001, false, entry.ProcessId); // PROCESS_TERMINATE
                    log.Mark("terminate", ("pid", entry.ProcessId), ("ok", handle != 0 && Native.TerminateProcess(handle, 1)));
                    Native.CloseHandle(handle);
                }
            }
        }

        Native.CloseHandle(snapshot);
        if (killed.Count > 0 || !kill)
        {
            log.Mark("browser-children", ("pids", string.Join(",", killed)), ("killed", kill));
        }

        return killed.Count;
    }

    private void Cleanup()
    {
        if (_controller != 0)
        {
            WebView2.CloseController(_controller);
            Marshal.Release(_controller);
            _controller = 0;
        }

        if (_environment != 0)
        {
            Marshal.Release(_environment);
            _environment = 0;
        }

        if (_window != 0)
        {
            Native.DestroyWindow(_window);
            _window = 0;
        }
    }
}
