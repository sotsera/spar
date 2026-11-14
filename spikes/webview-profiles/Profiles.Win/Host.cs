using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Spar.WebViewProfiles.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.WebViewProfiles;

/// <summary>
/// The app under test. Every window is offscreen and never activated. Each distinct user-data folder (UDF) gets one
/// environment; each window gets one controller with its own profile options. The page reports through
/// <c>POST /_spar/result</c>. Lines on stdout are <c>KEY k=v ...</c>, with <c>ft=</c> a UTC FILETIME so the harness can
/// compare host events with kernel process exit times.
/// </summary>
internal sealed class Host
{
    private const string Origin = "https://app.localhost";
    private const int GeolocationPermission = 3;
    private const int PermissionAllow = 1;

    private static Host? s_current;

    private readonly Options _options;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, Env> _environments = [];
    private readonly List<View> _views = [];
    private readonly TaskCompletionSource<string> _go = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HWND _messageWindow;
    private UiContext? _ui;
    private int _exitCode;

    private Host(Options options) => _options = options;

    public static int Run(Options options)
    {
        var host = new Host(options);
        var thread = new Thread(host.RunOnSta) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Log("HOST-EXIT", $"code={host._exitCode}");
        return host._exitCode;
    }

    public static long Now => DateTime.UtcNow.ToFileTimeUtc();

    public static void Log(string key, string text)
    {
        Console.Out.WriteLine($"{key} ft={Now} {text}");
        Console.Out.Flush();
    }

    private void RunOnSta()
    {
        s_current = this;
        CreateMessageWindow();
        SynchronizationContext.SetSynchronizationContext(_ui);
        StartWatchdog();
        if (_options.Has("handshake"))
        {
            var stdin = new Thread(() =>
            {
                var line = Console.In.ReadLine() ?? "eof";
                _ui!.Post(_ => _go.TrySetResult(line), null);
            }) { IsBackground = true };
            stdin.Start();
        }

        _ui!.Post(static async state => await ((Host)state!).MainAsync(), this);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    private void StartWatchdog()
    {
        var seconds = _options.Int("timeout", 90);
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            Log("TIMEOUT", $"after={seconds}s");
            Environment.Exit(3);
        }) { IsBackground = true };
        watchdog.Start();
    }

    private async Task MainAsync()
    {
        try
        {
            var scenario = _options.Get("scenario") ?? "shutdown";
            switch (scenario)
            {
                case "shutdown":
                    await StartViewsAsync();
                    if (_options.Int("pre-delay", 0) is var preDelay and > 0)
                    {
                        await Task.Delay(preDelay);
                    }

                    if (_options.Get("page") != "none")
                    {
                        await _views[0].NavigateAsync(_options.Get("page") ?? "op=read");
                    }

                    break;
                case "memory":
                    await StartViewsAsync();
                    await MemoryAsync();
                    break;
                case "multi":
                    await StartViewsAsync();
                    await MultiAsync();
                    break;
                case "clear":
                    await StartViewsAsync();
                    await ClearAsync();
                    break;
                case "udfdelete":
                    await StartViewsAsync();
                    await _views[0].NavigateAsync(_options.Get("page") ?? "op=readwrite&token=udf&bulk=4096");
                    break;
                case "tests":
                    await TestsAsync();
                    return;
                case "scan":
                    Log("TOKEN-SCAN", $"label=offline files=[{string.Join(';', Scan.FilesContaining(Paths.Udf(_options.Get("udf")!), _options.Get("token")!))}]");
                    PInvoke.PostQuitMessage(0);
                    return;
                case "private":
                    await StartViewsAsync();
                    await PrivateAsync();
                    break;
                case "names":
                    await NamesAsync();
                    break;
                default:
                    throw new InvalidOperationException("Unknown scenario " + scenario);
            }

            Log("READY", $"views={_views.Count}");
            if (_options.Has("handshake"))
            {
                var line = await _go.Task;
                Log("GO", $"line={line}");
            }
            else if (_options.Int("stay", 0) is var stay and > 0)
            {
                await Task.Delay(stay);
            }

            await ShutdownAsync();
        }
        catch (Exception exception)
        {
            Log("HOST-FAILURE", exception.ToString().ReplaceLineEndings(" | "));
            _exitCode = exception is ComFailureException { Message: var message } && message.StartsWith("CreateCoreWebView2Environment", StringComparison.Ordinal) ? 4 : 2;
            await ShutdownAsync();
        }
    }

    // ---- Startup -------------------------------------------------------------------------------------------------

    private async Task StartViewsAsync()
    {
        // --windows "udf/profile,..." where profile is "default", a ProfileName, or "~private" (InPrivate).
        var specs = (_options.Get("windows") ?? (_options.Get("udf") ?? "default") + "/default").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var spec in specs)
        {
            var parts = spec.Split('/');
            var env = await EnvironmentAsync(parts[0]);
            var profile = parts.Length > 1 ? parts[1] : "default";
            _views.Add(await View.CreateAsync(this, env, _views.Count, profile));
        }
    }

    private async Task<Env> EnvironmentAsync(string udfName)
    {
        if (_environments.TryGetValue(udfName, out var existing))
        {
            return existing;
        }

        var path = Paths.Udf(udfName);
        // Shortcuts for browser arguments that contain spaces or quotes (the harness splits --host on spaces).
        var browserArguments = new List<string>();
        if (_options.Get("browser-args") is { Length: > 0 } custom) browserArguments.Add(custom);
        if (_options.Has("block-network")) browserArguments.Add("--host-resolver-rules=\"MAP * ~NOTFOUND\"");
        if (_options.Has("block-app-host")) browserArguments.Add("--host-resolver-rules=\"MAP app.localhost ~NOTFOUND\"");
        if (_options.Get("netlog") is { Length: > 0 } netlog) browserArguments.Add($"--log-net-log=\"{Path.Combine(Paths.Output, netlog)}\" --net-log-capture-mode=Default");
        var options = new EnvironmentOptions
        {
            AdditionalBrowserArguments = string.Join(' ', browserArguments),
            Language = _options.Get("language") ?? "",
            Exclusive = _options.Get("exclusive") == "1",
        };
        var started = Stopwatch.GetTimestamp();
        var handler = new EnvironmentCompletedHandler();
        var hr = NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, path, options, handler);
        if (hr < 0)
        {
            Log("ENV-FAIL", $"udf={udfName} stage=call hr=0x{hr:X8} ms={Ms(started):F1}");
            throw new ComFailureException("CreateCoreWebView2EnvironmentWithOptions", hr);
        }

        ICoreWebView2Environment10 environment;
        try
        {
            environment = await handler.Task;
        }
        catch (ComFailureException failure)
        {
            Log("ENV-FAIL", $"udf={udfName} stage=completed hr=0x{failure.HResult:X8} ms={Ms(started):F1}");
            throw;
        }

        var env = new Env(udfName, path, environment);
        env.ExitedHandler = new BrowserProcessExitedHandler((kind, pid) =>
        {
            env.Exited.TrySetResult(Now);
            Log("BROWSER-EXITED", $"udf={udfName} kind={kind} pid={pid}");
        });
        env.ExitedToken = environment.AddBrowserProcessExited(env.ExitedHandler);
        Log("ENV", $"udf={udfName} ms={Ms(started):F1} version={environment.GetBrowserVersionString()} folder={environment.GetUserDataFolder()} " +
            $"args=[{options.AdditionalBrowserArguments}] language=[{options.Language}] exclusive={options.Exclusive}");
        _environments[udfName] = env;
        return env;
    }

    internal unsafe HWND CreateViewWindow(int index)
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparProfilesSpike")
        fixed (char* title = "Profiles spike " + index)
        {
            // Offscreen and never activated: runs must not take focus from the user or show anything.
            var window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, title,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 640, 480, HWND.Null, HMENU.Null, instance, null);
            if (window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }

            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            return window;
        }
    }

    private unsafe void CreateMessageWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparProfilesSpike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            // HWND_MESSAGE: a message-only window that carries the UI context's posted work.
            _messageWindow = PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, new HWND(-3), HMENU.Null, instance, null);
        }

        _ui = new UiContext(_messageWindow);
    }

    // ---- Scenarios -----------------------------------------------------------------------------------------------

    private async Task MemoryAsync()
    {
        foreach (var view in _views)
        {
            await view.NavigateAsync("op=read");
        }

        await Task.Delay(_options.Int("settle", 3000));
        SampleMemory("loaded");
    }

    private void SampleMemory(string label)
    {
        long totalPrivate = 0, totalWorkingSet = 0;
        var kinds = new SortedDictionary<string, (int Count, long Private)>();
        foreach (var env in _environments.Values)
        {
            foreach (var sample in ProcessTools.Memory(Paths.Marker(env.Name)))
            {
                totalPrivate += sample.PrivateBytes;
                totalWorkingSet += sample.WorkingSet;
                var kind = kinds.GetValueOrDefault(sample.Kind);
                kinds[sample.Kind] = (kind.Count + 1, kind.Private + sample.PrivateBytes);
            }
        }

        using var self = Process.GetCurrentProcess();
        var detail = string.Join(' ', kinds.Select(k => $"{k.Key}={k.Value.Count}x{k.Value.Private / 1048576.0:F1}"));
        Log("MEM", $"label={label} views={_views.Count} udfs={_environments.Count} browserPrivateMB={totalPrivate / 1048576.0:F1} " +
            $"browserWorkingSetMB={totalWorkingSet / 1048576.0:F1} hostPrivateMB={self.PrivateMemorySize64 / 1048576.0:F1} {detail}");
    }

    private async Task MultiAsync()
    {
        foreach (var view in _views)
        {
            view.LogProfile();
        }

        foreach (var view in _views)
        {
            await view.NavigateAsync("op=read");
        }

        // View 0 writes; its profile also gets a stored permission.
        await _views[0].NavigateAsync("op=readwrite&token=W0");
        var permission = new HresultCompletedHandler();
        _views[0].Profile.SetPermissionState(GeolocationPermission, Origin, PermissionAllow, permission);
        Log("SET-PERMISSION", $"view=0 hr=0x{await permission.Task:X8}");
        foreach (var view in _views.Skip(1))
        {
            await view.NavigateAsync("op=read");
        }

        // The InPrivate view (if any) writes; then every view reads again.
        if (_views.FirstOrDefault(v => v.InPrivate) is { } priv)
        {
            var writeStart = DateTime.UtcNow;
            await priv.NavigateAsync("op=readwrite&token=PRIVx7q9&bulk=512");
            await Task.Delay(1500);
            SnapshotFolders("inprivate-written");
            foreach (var env in _environments.Values)
            {
                Log("TOKEN-SCAN", $"label=inprivate-running udf={env.Name} files=[{string.Join(';', Scan.FilesContaining(env.Path, "PRIVx7q9"))}]");
                Log("WRITTEN-SINCE", $"label=inprivate-running udf={env.Name} files=[{string.Join(';', Scan.WrittenSince(env.Path, writeStart))}]");
            }
            foreach (var view in _views.Where(v => v != priv))
            {
                await view.NavigateAsync("op=read");
            }
        }

        foreach (var view in _views)
        {
            var settings = new PermissionSettingsHandler();
            view.Profile.GetNonDefaultPermissionSettings(settings);
            Log("PERMISSIONS", $"view={view.Index} profile={view.ProfileSpec} settings=[{string.Join(';', await settings.Task)}]");
        }
    }

    private void SnapshotFolders(string label)
    {
        foreach (var env in _environments.Values)
        {
            var root = Path.Combine(env.Path, "EBWebView");
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.GetDirectories(root))
            {
                var (files, bytes) = Paths.Size(directory);
                Log("FOLDER", $"label={label} udf={env.Name} dir={Path.GetFileName(directory)} files={files} kB={bytes / 1024}");
            }
        }
    }

    private async Task ClearAsync()
    {
        var view = _views[0];
        view.LogProfile();
        await view.NavigateAsync(_options.Get("page") ?? "op=readwrite&token=CLRz8w3&bulk=4096");
        await Task.Delay(_options.Int("settle", 2000));
        var profilePath = view.Profile.GetProfilePath();
        LogSize("before", profilePath);

        var method = _options.Get("clear") ?? "all";
        var started = Stopwatch.GetTimestamp();
        if (method == "delete")
        {
            await DeleteProfileAsync(view, profilePath);
            return;
        }

        var completed = new HresultCompletedHandler();
        if (method == "all")
        {
            view.Profile.ClearBrowsingDataAll(completed);
        }
        else
        {
            view.Profile.ClearBrowsingData(int.Parse(method, NumberStyles.HexNumber, CultureInfo.InvariantCulture), completed);
        }

        var hr = await completed.Task;
        Log("CLEARED", $"method={method} hr=0x{hr:X8} ms={Ms(started):F1}");
        LogSize("after-clear", profilePath);
        await Task.Delay(2000);
        LogSize("after-clear+2s", profilePath);
        await view.NavigateAsync("op=read");
        var settings = new PermissionSettingsHandler();
        view.Profile.GetNonDefaultPermissionSettings(settings);
        Log("PERMISSIONS", $"view=0 settings=[{string.Join(';', await settings.Task)}]");
        Log("FILES", $"label=after-clear {Paths.Listing(profilePath, 40)}");
        Log("TOKEN-SCAN", $"label=after-clear files=[{string.Join(';', Scan.FilesContaining(profilePath, "CLRz8w3"))}]");
    }

    private async Task DeleteProfileAsync(View view, string profilePath)
    {
        var started = Stopwatch.GetTimestamp();
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ProfileDeletedHandler(() => deleted.TrySetResult());
        var token = view.Profile.AddDeleted(handler);
        view.Profile.Delete();
        Log("DELETE-CALLED", $"ms={Ms(started):F1} exists={Directory.Exists(profilePath)}");
        var winner = await Task.WhenAny(deleted.Task, Task.Delay(10000));
        Log("DELETED", $"event={winner == deleted.Task} ms={Ms(started):F1} exists={Directory.Exists(profilePath)}");
        LogSize("after-deleted-event", profilePath);
        view.Closed = true;
        GC.KeepAlive(handler);
        _ = token;

        // Recreate the same name at once, then retry until it works or 5 s pass.
        for (var attempt = 0; attempt < 21; attempt++)
        {
            try
            {
                var again = await View.CreateAsync(this, view.Env, _views.Count, view.ProfileSpec);
                _views.Add(again);
                Log("RECREATE", $"attempt={attempt} ok ms={Ms(started):F1} exists={Directory.Exists(profilePath)}");
                await again.NavigateAsync("op=read");
                break;
            }
            catch (ComFailureException failure)
            {
                Log("RECREATE", $"attempt={attempt} hr=0x{failure.HResult:X8} ms={Ms(started):F1} exists={Directory.Exists(profilePath)}");
                await Task.Delay(250);
            }
        }

        LogSize("before-exit", profilePath);
        _deletedProfilePath = profilePath;
    }

    private string? _deletedProfilePath;

    private static void LogSize(string label, string path)
    {
        var (files, bytes) = Paths.Size(path);
        Log("SIZE", $"label={label} exists={Directory.Exists(path)} files={files} kB={bytes / 1024} path={path}");
    }

    private async Task PrivateAsync()
    {
        // Views: 0 = a regular profile that keeps the browser alive, 1 and 2 = InPrivate on the same profile.
        foreach (var view in _views)
        {
            view.LogProfile();
        }

        await _views[1].NavigateAsync("op=readwrite&token=PRIVa");
        await _views[2].NavigateAsync("op=read");
        await _views[0].NavigateAsync("op=read");
        var spec = _views[1].ProfileSpec;
        _views[1].Close();
        await _views[2].NavigateAsync("op=read");
        _views[2].Close();
        await Task.Delay(1000);
        var again = await View.CreateAsync(this, _views[1].Env, _views.Count, spec);
        _views.Add(again);
        again.LogProfile();
        await again.NavigateAsync("op=read");
    }

    private async Task NamesAsync()
    {
        // Profile-name rules: length and charset errors, and case-insensitivity.
        var env = await EnvironmentAsync(_options.Get("udf") ?? "names");
        string[] names = ["Alpha", "alpha", new string('a', 64), new string('a', 65), "with space", "dot.name", "a/b", "émoji", "under_score-dash", "#hash"];
        foreach (var name in names)
        {
            try
            {
                var view = await View.CreateAsync(this, env, _views.Count, name);
                _views.Add(view);
                Log("NAME", $"name=[{name}] ok path={view.Profile.GetProfilePath()} reported=[{view.Profile.GetProfileName()}]");
            }
            catch (Exception exception)
            {
                Log("NAME", $"name=[{name}] fail hr=0x{exception.HResult:X8} {exception.GetType().Name}");
            }
        }
    }

    private async Task TestsAsync()
    {
        // In-process "test" loop: each iteration needs a fresh profile.
        var mode = _options.Get("mode") ?? "freshprofile";
        var count = _options.Int("count", 10);
        var baseName = _options.Get("udf") ?? "tests";
        Env? shared = null;
        if (mode != "freshudf")
        {
            var started = Stopwatch.GetTimestamp();
            shared = await EnvironmentAsync(baseName);
            Log("TEST-ENV", $"ms={Ms(started):F1}");
        }

        for (var i = 0; i < count; i++)
        {
            var begin = Stopwatch.GetTimestamp();
            var env = shared;
            double envMs = 0;
            if (env is null)
            {
                var udfName = $"{baseName}-{_runId}-{i}";
                env = await EnvironmentAsync(udfName);
                envMs = Ms(begin);
            }

            var profile = mode switch
            {
                "inprivate" => "~private",
                "freshprofile" => $"t{_runId}{i}",
                _ => "default",
            };
            var controllerStart = Stopwatch.GetTimestamp();
            var view = await View.CreateAsync(this, env, _views.Count, profile);
            _views.Add(view);
            var controllerMs = Ms(controllerStart);
            var pageStart = Stopwatch.GetTimestamp();
            var result = await view.NavigateAsync($"op=readwrite&token=T{i}");
            var pageMs = Ms(pageStart);
            var fresh = result.Contains("\"ls\":null", StringComparison.Ordinal) && result.Contains("\"idb\":null", StringComparison.Ordinal);
            var cleanupStart = Stopwatch.GetTimestamp();
            string cleanup;
            if (mode == "freshprofile")
            {
                var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var handler = new ProfileDeletedHandler(() => deleted.TrySetResult());
                view.Profile.AddDeleted(handler);
                view.Profile.Delete();
                var ok = await Task.WhenAny(deleted.Task, Task.Delay(5000)) == deleted.Task;
                view.Closed = true;
                GC.KeepAlive(handler);
                view.Close();
                cleanup = $"deleted-event={ok} cleanupMs={Ms(cleanupStart):F1}";
            }
            else if (mode == "inprivate")
            {
                view.Close();
                cleanup = $"cleanupMs={Ms(cleanupStart):F1}";
            }
            else
            {
                view.Close();
                var exited = await Task.WhenAny(env.Exited.Task, Task.Delay(20000)) == env.Exited.Task;
                var exitMs = Ms(cleanupStart);
                var gone = await WaitForNoProcessesAsync(env.Name, 20000);
                var goneMs = Ms(cleanupStart);
                var deleteStart = Stopwatch.GetTimestamp();
                var deletedOk = gone && Paths.TryDelete(env.Path);
                cleanup = $"browser-exited={exited} exitMs={exitMs:F1} gone={gone} goneMs={goneMs:F1} deleted={deletedOk} deleteMs={Ms(deleteStart):F1}";
                _environments.Remove(env.Name);
            }

            Log("TEST", $"mode={mode} i={i} fresh={fresh} envMs={envMs:F1} controllerMs={controllerMs:F1} pageMs={pageMs:F1} {cleanup} totalMs={Ms(begin):F1}");
        }

        await ShutdownAsync();
    }

    // ---- Shutdown ------------------------------------------------------------------------------------------------

    private async Task ShutdownAsync()
    {
        Log("CLOSE-START", $"views={_views.Count}");
        foreach (var view in _views)
        {
            view.Close();
        }

        Log("CLOSE-END", "");
        var exit = _options.Get("exit") ?? "nowait";
        if (exit == "wait")
        {
            foreach (var env in _environments.Values)
            {
                var exited = await Task.WhenAny(env.Exited.Task, Task.Delay(20000)) == env.Exited.Task;
                Log("WAITED", $"udf={env.Name} browser-exited={exited}");
            }

            if (_deletedProfilePath is { } path)
            {
                LogSize("after-browser-exit", path);
            }

            if (_options.Get("scenario") == "udfdelete")
            {
                foreach (var env in _environments.Values)
                {
                    var started = Stopwatch.GetTimestamp();
                    var gone = await WaitForNoProcessesAsync(env.Name, 20000);
                    Log("ALL-GONE", $"udf={env.Name} gone={gone} ms={Ms(started):F1}");
                    var deleteStart = Stopwatch.GetTimestamp();
                    var deleted = gone && Paths.TryDelete(env.Path);
                    Log("UDF-DELETED", $"udf={env.Name} ok={deleted} ms={Ms(deleteStart):F1}");
                }
            }
        }

        foreach (var view in _views)
        {
            view.DestroyWindow();
        }

        PInvoke.PostQuitMessage(0);
    }

    private static async Task<bool> WaitForNoProcessesAsync(string udfName, int timeoutMilliseconds)
    {
        var deadline = Stopwatch.GetTimestamp() + timeoutMilliseconds * Stopwatch.Frequency / 1000;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (ProcessTools.Find(Paths.Marker(udfName), keepHandles: false).Count == 0)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    internal string RunId => _runId;

    internal Options Options => _options;

    internal static double Ms(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case UiContext.DrainMessage:
                    s_current?._ui?.Drain();
                    return default;
                case PInvoke.WM_CLOSE:
                    // Windows are closed by the host only.
                    return default;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    internal sealed class Env(string name, string path, ICoreWebView2Environment10 environment)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;

        public ICoreWebView2Environment10 Environment { get; } = environment;

        public TaskCompletionSource<long> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BrowserProcessExitedHandler? ExitedHandler { get; set; }

        public EventRegistrationToken ExitedToken { get; set; }
    }

    internal sealed class View
    {
        private readonly Host _host;
        private readonly HWND _window;
        private WebResourceRequestedHandler? _resourceHandler;
        private EventRegistrationToken _resourceToken;
        private TaskCompletionSource<string>? _result;

        private View(Host host, Env env, int index, string profileSpec, HWND window)
        {
            _host = host;
            Env = env;
            Index = index;
            ProfileSpec = profileSpec;
            _window = window;
        }

        public Env Env { get; }

        public int Index { get; }

        public string ProfileSpec { get; }

        public bool InPrivate => ProfileSpec.StartsWith("~private", StringComparison.Ordinal);

        public ICoreWebView2Controller? Controller { get; private set; }

        public ICoreWebView2 WebView { get; private set; } = null!;

        public ICoreWebView2Profile8 Profile { get; private set; } = null!;

        public bool Closed { get; set; }

        public static async Task<View> CreateAsync(Host host, Env env, int index, string profileSpec)
        {
            var window = host.CreateViewWindow(index);
            var view = new View(host, env, index, profileSpec, window);
            try
            {
                await view.InitializeAsync();
            }
            catch
            {
                view.DestroyWindow();
                throw;
            }

            return view;
        }

        private async Task InitializeAsync()
        {
            var started = Stopwatch.GetTimestamp();
            var handler = new ControllerCompletedHandler();
            if (ProfileSpec == "default")
            {
                Env.Environment.CreateCoreWebView2Controller(WindowHandle, handler);
            }
            else
            {
                var options = Env.Environment.CreateCoreWebView2ControllerOptions();
                if (InPrivate)
                {
                    // "~private" is InPrivate on the default profile; "~private:NAME" is InPrivate on profile NAME.
                    options.PutIsInPrivateModeEnabled(1);
                    if (ProfileSpec.Length > 9)
                    {
                        options.PutProfileName(ProfileSpec[9..]);
                    }
                }
                else
                {
                    options.PutProfileName(ProfileSpec);
                }

                Env.Environment.CreateCoreWebView2ControllerWithOptions(WindowHandle, options, handler);
            }

            try
            {
                Controller = await handler.Task;
            }
            catch (ComFailureException failure)
            {
                Log("CONTROLLER-FAIL", $"view={Index} profile={ProfileSpec} hr=0x{failure.HResult:X8} ms={Ms(started):F1}");
                throw;
            }

            WebView = Controller.GetCoreWebView2();
            Profile = ((ICoreWebView2_13)WebView).GetProfile();
            PInvoke.GetClientRect(_window, out var client);
            Controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
            var settings = WebView.GetSettings();
            settings.PutAreDefaultScriptDialogsEnabled(0);
            if (_host.Options.Get("reputation") == "off")
            {
                settings.PutIsReputationCheckingRequired(0);
            }

            _resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
            _resourceToken = WebView.AddWebResourceRequested(_resourceHandler);
            ((ICoreWebView2_22)WebView).AddWebResourceRequestedFilterWithRequestSourceKinds(Origin + "/*", 0, unchecked((int)0xFFFFFFFF));
            Log("CONTROLLER", $"view={Index} udf={Env.Name} profile={ProfileSpec} ms={Ms(started):F1} browserPid={WebView.GetBrowserProcessId()} " +
                $"reputation={settings.GetIsReputationCheckingRequired()}");
        }

        public void LogProfile() =>
            Log("PROFILE", $"view={Index} spec={ProfileSpec} name=[{Profile.GetProfileName()}] inprivate={Profile.GetIsInPrivateModeEnabled()} path={Profile.GetProfilePath()}");

        public async Task<string> NavigateAsync(string query)
        {
            _result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = Stopwatch.GetTimestamp();
            WebView.Navigate($"{Origin}/?{query}&w={Index}");
            var result = await _result.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Log("RESULT", $"view={Index} profile={ProfileSpec} ms={Ms(started):F1} json={result}");
            return result;
        }

        public void Close()
        {
            if (Controller is null)
            {
                return;
            }

            if (!Closed && _resourceHandler is not null)
            {
                try
                {
                    WebView.RemoveWebResourceRequested(_resourceToken);
                }
                catch (Exception exception)
                {
                    Log("REMOVE-HANDLER", $"view={Index} {exception.GetType().Name} 0x{exception.HResult:X8}");
                }
            }

            var hr = Controller.Close();
            if (hr < 0)
            {
                Log("CLOSE-HR", $"view={Index} hr=0x{hr:X8}");
            }

            Controller = null;
            _resourceHandler = null;
        }

        public void DestroyWindow()
        {
            if (!_window.IsNull)
            {
                PInvoke.DestroyWindow(_window);
            }
        }

        private unsafe nint WindowHandle => (nint)_window.Value;

        private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
        {
            var request = args.GetRequest();
            var uri = new Uri(request.GetUri());
            var path = uri.AbsolutePath;
            switch (path)
            {
                case "/_spar/result":
                    var body = ReadBody(request);
                    args.PutResponse(Respond([], 204, "No Content", "text/plain"));
                    _result?.TrySetResult(body);
                    return;
                case "/cacheable.txt":
                    Log("REQ", $"view={Index} path={path}");
                    args.PutResponse(Respond("cacheable"u8, 200, "OK", "text/plain", "\r\nX-Serial: " + _host.RunId, cache: "public, max-age=86400"));
                    return;
            }

            var key = "www/" + (path == "/" ? "index.html" : path[1..]);
            using var stream = typeof(Host).Assembly.GetManifestResourceStream(key);
            if (stream is null)
            {
                args.PutResponse(Respond("not found"u8, 404, "Not Found", "text/plain"));
                return;
            }

            var content = new byte[stream.Length];
            stream.ReadExactly(content);
            var type = Path.GetExtension(key) == ".js" ? "text/javascript; charset=utf-8" : "text/html; charset=utf-8";
            args.PutResponse(Respond(content, 200, "OK", type, key == "www/sw.js" ? "\r\nService-Worker-Allowed: /" : ""));
        }

        private static unsafe string ReadBody(ICoreWebView2WebResourceRequest request)
        {
            if (request.GetContent() is not { } content)
            {
                return "";
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            while (true)
            {
                uint read = 0;
                fixed (byte* data = chunk)
                {
                    content.Read((nint)data, (uint)chunk.Length, (nint)(&read));
                }

                if (read == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, (int)read);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType, string extraHeaders = "", string cache = "no-store")
        {
            nint stream;
            fixed (byte* data = content)
            {
                stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
            }

            if (stream == 0)
            {
                throw new OutOfMemoryException("SHCreateMemStream failed.");
            }

            try
            {
                return Env.Environment.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: {cache}{extraHeaders}");
            }
            finally
            {
                Marshal.Release(stream);
            }
        }
    }
}
