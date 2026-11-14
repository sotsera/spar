using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Spar.ResourceUsage.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.ResourceUsage;

/// <summary>
/// The Windows app under test: one offscreen, never-activated window with one WebView2 controller on a user-data folder
/// (UDF) under artifacts/webview2-profiles/resource-usage/. Scenarios: memory, disk, shared. The page reports through
/// chrome.webview.postMessage. Every measurement is a log line on stdout.
/// </summary>
internal sealed class Host
{
    private const string Origin = "https://app.localhost";
    private static Host? s_current;

    private readonly Options _options;
    private readonly string _udf;
    private HWND _messageWindow;
    private HWND _window;
    private UiContext? _ui;
    private int _uiThreadId;
    private int _exitCode;
    private object? _environmentObject;
    private ICoreWebView2Environment8 _environment = null!;
    private ICoreWebView2Environment13? _environment13;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2 _webView = null!;
    private TaskCompletionSource<string>? _message;
    private readonly TaskCompletionSource _browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<object> _keepAlive = [];
    private EngineProcesses? _engine;
    private int _processInfosChanged;
    private readonly List<double> _pings = [];

    private Host(Options options)
    {
        _options = options;
        _udf = Paths.Udf(options.Get("udf") ?? "default");
    }

    public static int Run(Options options)
    {
        Win32.AssertLayouts();
        var host = new Host(options);
        var thread = new Thread(host.RunOnSta) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Log.Line("HOST-EXIT", $"code={host._exitCode}");
        return host._exitCode;
    }

    private void RunOnSta()
    {
        s_current = this;
        _uiThreadId = Environment.CurrentManagedThreadId;
        CreateWindows();
        SynchronizationContext.SetSynchronizationContext(_ui);
        var timeout = _options.Int("timeout", 180);
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(timeout));
            Log.Line("TIMEOUT", $"after={timeout}s");
            Environment.Exit(3);
        }) { IsBackground = true }.Start();
        _ui!.Post(static async state => await ((Host)state!).MainAsync(), this);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    private async Task MainAsync()
    {
        try
        {
            await StartAsync();
            switch (_options.Get("scenario") ?? "memory")
            {
                case "memory":
                    await NavigateAsync(Origin + "/small.html");
                    await Task.Delay(_options.Int("settle", 3000));
                    await MemoryAsync();
                    break;
                case "disk":
                    await DiskAsync();
                    break;
                case "shared":
                    await NavigateAsync(Origin + "/small.html?instance=" + (_options.Get("instance") ?? "a"));
                    await SharedAsync();
                    break;
                default:
                    throw new InvalidOperationException("Unknown scenario.");
            }
        }
        catch (Exception exception)
        {
            Log.Line("HOST-FAILURE", exception.ToString().ReplaceLineEndings(" | "));
            _exitCode = 2;
        }

        await ShutdownAsync();
    }

    // ---- Start-up ------------------------------------------------------------------------------------------------

    private async Task StartAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var handler = new EnvironmentCompletedHandler();
        var hr = NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, _udf, 0, handler);
        if (hr < 0)
        {
            throw new ComFailureException("CreateCoreWebView2EnvironmentWithOptions", hr);
        }

        _environmentObject = await handler.Task;
        // Feature detection by QueryInterface: Environment8 has GetProcessInfos, Environment13 GetProcessExtendedInfos.
        _environment = _environmentObject as ICoreWebView2Environment8 ?? throw new InvalidOperationException("ICoreWebView2Environment8 missing.");
        _environment13 = _environmentObject as ICoreWebView2Environment13;
        Log.Line("ENV", $"ms={Log.Ms(started):F1} version={_environment.GetBrowserVersionString()} env8=True env13={_environment13 is not null} folder={_environment.GetUserDataFolder()}");

        var exited = new BrowserProcessExitedHandler(() => _browserExited.TrySetResult());
        var changed = new ProcessInfosChangedHandler(OnProcessInfosChanged);
        _keepAlive.Add(exited);
        _keepAlive.Add(changed);
        _environment.AddBrowserProcessExited(exited);
        _environment.AddProcessInfosChanged(changed);

        var controllerHandler = new ControllerCompletedHandler();
        unsafe
        {
            _environment.CreateCoreWebView2Controller((nint)_window.Value, controllerHandler);
        }

        _controller = await controllerHandler.Task;
        _webView = _controller.GetCoreWebView2();
        PInvoke.GetClientRect(_window, out var client);
        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
        var settings = _webView.GetSettings();
        settings.PutAreDefaultScriptDialogsEnabled(0);
        // No SmartScreen request per navigation, and no 6.4 s exit after a young close (spikes/webview-profiles).
        settings.PutIsReputationCheckingRequired(0);
        var resources = new WebResourceRequestedHandler(OnWebResourceRequested);
        var messages = new WebMessageReceivedHandler(text => _message?.TrySetResult(text));
        _keepAlive.Add(resources);
        _keepAlive.Add(messages);
        _webView.AddWebResourceRequested(resources);
        ((ICoreWebView2_22)_webView).AddWebResourceRequestedFilterWithRequestSourceKinds(Origin + "/*", 0, unchecked((int)0xFFFFFFFF));
        _webView.AddWebMessageReceived(messages);
        Log.Line("CONTROLLER", $"ms={Log.Ms(started):F1} browserPid={_webView.GetBrowserProcessId()} profile={((ICoreWebView2_13)_webView).GetProfile().GetProfilePath()}");
    }

    private void OnProcessInfosChanged()
    {
        _processInfosChanged++;
        Log.Line("PROCESS-INFOS-CHANGED", $"count={_processInfosChanged} onUi={Environment.CurrentManagedThreadId == _uiThreadId}");
        _engine?.Refresh();
    }

    private async Task<string> NavigateAsync(string uri)
    {
        _message = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Stopwatch.GetTimestamp();
        _webView.Navigate(uri);
        var text = await _message.Task.WaitAsync(TimeSpan.FromSeconds(120));
        Log.Line("PAGE", $"uri={uri} ms={Log.Ms(started):F1} message={text}");
        return text;
    }

    // ---- Memory --------------------------------------------------------------------------------------------------

    private async Task MemoryAsync()
    {
        var iterations = _options.Int("iterations", 200);

        // 1. Which processes, and how much each listing costs.
        var listed = EngineProcesses.List(_environment);
        Log.Line("PROCESSES", "source=GetProcessInfos " + string.Join(' ', listed.Select(p => $"{EngineProcesses.KindName(p.Kind)}:{p.Pid}")));
        Log.Line("COST", $"what=GetProcessInfos+read-list thread=sta {Stats.Time(iterations, () => EngineProcesses.List(_environment))}");
        await ExtendedInfosAsync();
        var marker = Path.Combine(_udf, "EBWebView");
        var scanned = ScanByCommandLine(marker);
        Log.Line("PROCESSES", "source=command-line-scan " + string.Join(' ', scanned.Select(p => $"{p.Kind}:{p.Pid}")));
        var missing = scanned.Where(s => listed.All(l => l.Pid != s.Pid)).Select(s => $"{s.Kind}:{s.Pid}");
        Log.Line("PROCESSES", $"source=compare onlyInScan=[{string.Join(' ', missing)}] onlyInList=[{string.Join(' ', listed.Where(l => scanned.All(s => s.Pid != l.Pid)).Select(l => l.Pid))}]");
        Log.Line("COST", $"what=command-line-scan thread=pool {await Task.Run(() => Stats.Time(10, () => ScanByCommandLine(marker), "ms"))}");

        // 2. Thread affinity: the environment and the returned collection, called from a pool thread.
        var offList = await Task.Run(() =>
        {
            try
            {
                return $"hr=0x{_environment.GetProcessInfos(out _):X8}";
            }
            catch (Exception exception)
            {
                return $"exception={exception.GetType().Name} hr=0x{exception.HResult:X8}";
            }
        });
        _environment.GetProcessInfos(out var staCollection);
        var offCollection = await Task.Run(() =>
        {
            try
            {
                var hr = staCollection!.GetCount(out var count);
                var hr2 = staCollection.GetValueAtIndex(0, out var info);
                var hr3 = info is null ? -1 : info.GetProcessId(out _);
                return $"count-hr=0x{hr:X8} count={count} item-hr=0x{hr2:X8} pid-hr=0x{hr3:X8}";
            }
            catch (Exception exception)
            {
                return $"exception={exception.GetType().Name} hr=0x{exception.HResult:X8}";
            }
        });
        Log.Line("THREADING", $"GetProcessInfos-from-pool {offList}; collection-from-pool {offCollection}");

        // 3. Each candidate measure, per process.
        LogProcessMemory("loaded", listed);
        foreach (var (pid, _) in listed)
        {
            var limited = Win32.CountersOf(pid);
            Log.Line("ACCESS", $"pid={pid} limitedOnly={(limited.Counters is not null)} error={limited.Error}");
        }

        // 4. Cost of one full sample: on the STA, and split (list on the STA, read on a pool thread).
        Log.Line("COST", $"what=list+open+EX2+close thread=sta {Stats.Time(iterations, () => FullSample(_environment))}");
        var pids = listed.Select(p => p.Pid).ToArray();
        Log.Line("COST", $"what=open+EX2+close thread=pool {await Task.Run(() => Stats.Time(iterations, () => ReadOpen(pids)))}");
        _engine = new EngineProcesses(_environment);
        _engine.Refresh();
        Log.Line("COST", $"what=cached-handles+EX2 thread=pool {await Task.Run(() => Stats.Time(iterations, () => _engine.Read()))}");
        Log.Line("COST", $"what=cached-handles+EX2 thread=sta {Stats.Time(iterations, () => _engine.Read())}");
        Log.Line("COST", $"what=Process.GetProcessById.PrivateMemorySize64 thread=pool {await Task.Run(() => Stats.Time(20, () => DotNetSample(pids), "ms"))}");
        Log.Line("COST", $"what=QueryWorkingSet-all thread=pool {await Task.Run(() => Stats.Time(10, () => { foreach (var pid in pids) { Win32.WorkingSetPages(pid); } }, "ms"))}");

        // 5. What the UI thread sees: dispatch latency of a ping posted every ~1 ms, while sampling on or off the STA.
        await PingPhaseAsync("idle", 3000, null);
        await PingPhaseAsync("sample-on-sta-every-100ms", 3000, () => FullSample(_environment), onSta: true);
        await PingPhaseAsync("sample-on-pool-every-100ms", 3000, () => _engine.Read(), onSta: false);

        // 6. The backend's own memory.
        Backend.Report("host", 20,
        [
            ("ex2PrivateUsage", () => (long)Win32.Self().PrivateUsage),
            ("ex2PrivateWorkingSet", () => (long)Win32.Self().PrivateWorkingSetSize),
            ("ex2WorkingSet", () => (long)Win32.Self().WorkingSetSize),
        ]);

        // 7. The sketch sampler: 100 ms ticks, a reader that takes 300 ms per value, 5 s.
        var always = new LatestValueSampler<MemoryReading>(_ => ValueTask.FromResult(_engine.Read()), (_, _) => true, TimeSpan.FromMilliseconds(100));
        await Task.Run(() => LatestValueSampler<MemoryReading>.MeasureAsync("engine-every-tick", always, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300)));
        var sampler = new LatestValueSampler<MemoryReading>(
            _ => ValueTask.FromResult(_engine.Read()),
            (a, b) => LatestValueSampler<MemoryReading>.Beyond(a.Private, b.Private, 0.01, 1 << 20) || a.Processes != b.Processes,
            TimeSpan.FromMilliseconds(100));
        await Task.Run(() => LatestValueSampler<MemoryReading>.MeasureAsync("engine-threshold-1%-1MB", sampler, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300)));
        Log.Line("PROCESS-INFOS-CHANGED", $"total={_processInfosChanged} refreshes={_engine.Refreshes}");

        // 8. Snapshot bracket for the harness's performance-counter read (Task Manager's private working set).
        if (_options.Get("go-file") is { Length: > 0 } goFile)
        {
            LogProcessMemory("before-counters", EngineProcesses.List(_environment));
            Log.Line("READY", "");
            var waited = Stopwatch.GetTimestamp();
            while (!File.Exists(goFile) && Log.Ms(waited) < 60000)
            {
                await Task.Delay(50);
            }

            LogProcessMemory("after-counters", EngineProcesses.List(_environment));
            Log.Line("GO", $"found={File.Exists(goFile)}");
        }
    }

    private async Task ExtendedInfosAsync()
    {
        if (_environment13 is null)
        {
            Log.Line("EXTENDED", "available=False");
            return;
        }

        var times = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var handler = new ProcessExtendedInfosHandler();
            var hr = _environment13.GetProcessExtendedInfos(handler);
            var (completedHr, result) = await handler.Task;
            times.Add(Log.Ms(started));
            if (i > 0)
            {
                continue;
            }

            var lines = new List<string>();
            for (uint index = 0; index < (result?.GetCount() ?? 0); index++)
            {
                var item = result!.GetValueAtIndex(index);
                var info = item.GetProcessInfo();
                info.GetProcessId(out var pid);
                info.GetKind(out var kind);
                var frames = new List<string>();
                var iterator = item.GetAssociatedFrameInfos().GetIterator();
                while (iterator.GetHasCurrent() != 0)
                {
                    var frame = iterator.GetCurrent();
                    frames.Add($"[{frame.GetName()}]{frame.GetSource()}");
                    iterator.MoveNext();
                }

                lines.Add($"{EngineProcesses.KindName(kind)}:{pid}:frames={frames.Count}{(frames.Count > 0 ? ":" + string.Join(',', frames) : "")}");
            }

            Log.Line("EXTENDED", $"call-hr=0x{hr:X8} completed-hr=0x{completedHr:X8} {string.Join(' ', lines)}");
        }

        Log.Line("COST", $"what=GetProcessExtendedInfos-to-completion thread=sta {Stats.Of(times, "ms")}");
    }

    private static List<(int Pid, string Kind)> ScanByCommandLine(string marker)
    {
        var found = new List<(int, string)>();
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            using (process)
            {
                var commandLine = Win32.CommandLine(process.Id);
                if (commandLine is null || !commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var index = commandLine.IndexOf("--type=", StringComparison.Ordinal);
                var kind = index < 0 ? "browser" : commandLine[(index + 7)..].Split(' ')[0].Trim('"');
                if (kind == "utility" && commandLine.IndexOf("--utility-sub-type=", StringComparison.Ordinal) is var sub and >= 0)
                {
                    kind = "utility:" + commandLine[(sub + 19)..].Split(' ')[0].Split('.').Last().Trim('"');
                }

                found.Add((process.Id, kind));
            }
        }

        return found;
    }

    private void LogProcessMemory(string label, List<(int Pid, int Kind)> listed)
    {
        long ws = 0, privateWs = 0, privateUsage = 0, sharedCommit = 0, dotnetPrivate = 0, qwsPrivate = 0, qwsProportional = 0;
        foreach (var (pid, kind) in listed)
        {
            var (counters, error) = Win32.CountersOf(pid);
            var pages = Win32.WorkingSetPages(pid);
            long dotnet = -1;
            try
            {
                using var process = Process.GetProcessById(pid);
                dotnet = process.PrivateMemorySize64;
            }
            catch (ArgumentException)
            {
            }

            if (counters is { } c)
            {
                ws += (long)c.WorkingSetSize;
                privateWs += (long)c.PrivateWorkingSetSize;
                privateUsage += (long)c.PrivateUsage;
                sharedCommit += (long)c.SharedCommitUsage;
            }

            dotnetPrivate += Math.Max(0, dotnet);
            qwsPrivate += pages.PrivatePages * 4096;
            qwsProportional += (long)(pages.ProportionalPages * 4096);
            Log.Line("MEM", string.Create(CultureInfo.InvariantCulture,
                $"label={label} pid={pid} kind={EngineProcesses.KindName(kind)} ex2-error={error} workingSetKB={(long)(counters?.WorkingSetSize ?? 0) / 1024} " +
                $"privateWorkingSetKB={(long)(counters?.PrivateWorkingSetSize ?? 0) / 1024} privateUsageKB={(long)(counters?.PrivateUsage ?? 0) / 1024} " +
                $"sharedCommitKB={(long)(counters?.SharedCommitUsage ?? 0) / 1024} dotnetPrivateKB={dotnet / 1024} " +
                $"qws-error={pages.Error} qwsPages={pages.Pages} qwsPrivateKB={pages.PrivatePages * 4} qwsSharedKB={pages.SharedPages * 4} qwsSharedOnceKB={pages.SharedOncePages * 4} " +
                $"qwsProportionalKB={(long)(pages.ProportionalPages * 4)} qwsMs={pages.Ms:F2}"));
        }

        var self = Win32.Self();
        Log.Line("MEM-TOTAL", $"label={label} processes={listed.Count} workingSetMB={Log.Mb(ws)} privateWorkingSetMB={Log.Mb(privateWs)} privateUsageMB={Log.Mb(privateUsage)} " +
            $"sharedCommitMB={Log.Mb(sharedCommit)} dotnetPrivateMB={Log.Mb(dotnetPrivate)} qwsPrivateMB={Log.Mb(qwsPrivate)} qwsProportionalMB={Log.Mb(qwsProportional)} " +
            $"hostWorkingSetMB={Log.Mb((long)self.WorkingSetSize)} hostPrivateWorkingSetMB={Log.Mb((long)self.PrivateWorkingSetSize)} hostPrivateUsageMB={Log.Mb((long)self.PrivateUsage)} " +
            $"hostPid={Environment.ProcessId}");
    }

    private static long FullSample(ICoreWebView2Environment8 environment)
    {
        long total = 0;
        foreach (var (pid, _) in EngineProcesses.List(environment))
        {
            total += (long)(Win32.CountersOf(pid).Counters?.PrivateWorkingSetSize ?? 0);
        }

        return total;
    }

    private static long ReadOpen(int[] pids)
    {
        long total = 0;
        foreach (var pid in pids)
        {
            total += (long)(Win32.CountersOf(pid).Counters?.PrivateWorkingSetSize ?? 0);
        }

        return total;
    }

    private static long DotNetSample(int[] pids)
    {
        long total = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                total += process.PrivateMemorySize64;
            }
            catch (ArgumentException)
            {
            }
        }

        return total;
    }

    /// <summary>
    /// A pool thread posts a ping to the STA about every millisecond (the next one only after the last arrived) and
    /// the STA records how long each took to be dispatched. Meanwhile a sample runs every 100 ms on the STA or on a pool
    /// thread.
    /// </summary>
    private async Task PingPhaseAsync(string label, int milliseconds, Action? sample, bool onSta = false)
    {
        _pings.Clear();
        using var stop = new CancellationTokenSource(milliseconds);
        var sampleTimes = new List<double>();
        var pinger = Task.Run(() =>
        {
            var arrived = new ManualResetEventSlim(true);
            while (!stop.IsCancellationRequested)
            {
                arrived.Wait(stop.Token);
                arrived.Reset();
                var posted = Stopwatch.GetTimestamp();
                _ui!.Post(_ =>
                {
                    _pings.Add(Log.Ms(posted));
                    arrived.Set();
                }, null);
                var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
                while (Stopwatch.GetTimestamp() < until)
                {
                    Thread.SpinWait(50);
                }
            }
        });
        if (sample is not null)
        {
            if (onSta)
            {
                while (!stop.IsCancellationRequested)
                {
                    var started = Stopwatch.GetTimestamp();
                    sample();
                    sampleTimes.Add(Log.Ms(started));
                    try
                    {
                        await Task.Delay(100, stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
            else
            {
                await Task.Run(async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var started = Stopwatch.GetTimestamp();
                        sample();
                        sampleTimes.Add(Log.Ms(started));
                        try
                        {
                            await Task.Delay(100, stop.Token);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                });
            }
        }

        try
        {
            await pinger;
        }
        catch (OperationCanceledException)
        {
        }

        Log.Line("UI-PING", $"label={label} {Stats.Of([.. _pings], "ms")} samples={sampleTimes.Count} sampleCost=({Stats.Of(sampleTimes, "ms")})");
    }

    // ---- Shared folder -------------------------------------------------------------------------------------------

    private async Task SharedAsync()
    {
        var stay = _options.Int("stay", 10000);
        var every = _options.Int("every", 1000);
        var started = Stopwatch.GetTimestamp();
        while (Log.Ms(started) < stay)
        {
            var listed = EngineProcesses.List(_environment);
            Log.Line("SHARED-LIST", $"instance={_options.Get("instance")} t={Log.Ms(started):F0} browserPid={_webView.GetBrowserProcessId()} " +
                string.Join(' ', listed.Select(p => $"{EngineProcesses.KindName(p.Kind)}:{p.Pid}")));
            if (_environment13 is not null)
            {
                var handler = new ProcessExtendedInfosHandler();
                _environment13.GetProcessExtendedInfos(handler);
                var (_, result) = await handler.Task;
                var renderers = new List<string>();
                for (uint index = 0; index < (result?.GetCount() ?? 0); index++)
                {
                    var item = result!.GetValueAtIndex(index);
                    var iterator = item.GetAssociatedFrameInfos().GetIterator();
                    var frames = new List<string>();
                    while (iterator.GetHasCurrent() != 0)
                    {
                        frames.Add(iterator.GetCurrent().GetSource());
                        iterator.MoveNext();
                    }

                    item.GetProcessInfo().GetProcessId(out var pid);
                    if (frames.Count > 0)
                    {
                        renderers.Add($"{pid}=[{string.Join(',', frames)}]");
                    }
                }

                Log.Line("SHARED-FRAMES", $"instance={_options.Get("instance")} t={Log.Ms(started):F0} {string.Join(' ', renderers)}");
            }

            LogProcessMemory("shared-" + _options.Get("instance"), listed);
            await Task.Delay(every);
        }
    }

    // ---- Disk ----------------------------------------------------------------------------------------------------

    private async Task DiskAsync()
    {
        var server = _options.Get("server") ?? throw new InvalidOperationException("--server is required for the disk scenario.");
        var fill = _options.Get("fill") ?? "ls=1024&idb=8&cache=4&http=40&size=100";
        await NavigateAsync($"{server}/fill.html?{fill}");
        await Task.Delay(_options.Int("settle", 3000));
        var profile = ((ICoreWebView2_13)_webView).GetProfile().GetProfilePath();
        var iterations = _options.Int("iterations", 10);
        await Task.Run(() =>
        {
            WalkAll("running", _udf, profile, iterations);
            var (locked, total, ms) = LockedFiles(_udf);
            Log.Line("LOCKED", $"label=running files={total} cannotOpenForRead={locked} ms={ms:F1}");
        });
        await Task.Delay(_options.Int("later", 10000));
        await Task.Run(() => WalkAll("running-later", _udf, profile, 3));

        // The same walks after the browser has exited, to see what the running browser held back.
        CloseController();
        var exited = await Task.WhenAny(_browserExited.Task, Task.Delay(20000)) == _browserExited.Task;
        await Task.Delay(500);
        Log.Line("BROWSER-EXITED", $"ok={exited}");
        await Task.Run(() => WalkAll("exited", _udf, profile, 3));
    }

    private static void WalkAll(string label, string udf, string profile, int iterations)
    {
        (string Name, string Path)[] targets = [("udf", udf), ("profile", profile)];
        (string Name, Func<string, WalkResult> Walk)[] methods =
        [
            ("FileInfo", Walk.FileInfos),
            ("FileSystemEnumerator", Walk.Entries),
            ("FindFirstFileEx", Win32.FindWalk),
            ("FileFullDirectoryInfo", Win32.DirectoryInfoWalk),
            ("PerFileAttributes", Walk.PerFile),
        ];
        foreach (var (targetName, path) in targets)
        {
            foreach (var (methodName, walk) in methods)
            {
                var first = walk(path);
                var times = new List<double>();
                WalkResult last = first;
                for (var i = 0; i < iterations; i++)
                {
                    last = walk(path);
                    times.Add(last.Ms);
                }

                Log.Line("WALK", $"label={label} target={targetName} method={methodName} first=({first}) {last} repeat=({Stats.Of(times, "ms")})");
            }
        }

        // Top-level folders of the profile: where the bytes are.
        foreach (var directory in Directory.EnumerateDirectories(profile))
        {
            var result = Win32.DirectoryInfoWalk(directory);
            Log.Line("WALK-PART", $"label={label} folder={Path.GetFileName(directory)} {result}");
        }

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(udf, "EBWebView")).Where(d => !string.Equals(d, profile, StringComparison.OrdinalIgnoreCase)))
        {
            var result = Win32.DirectoryInfoWalk(directory);
            Log.Line("WALK-PART", $"label={label} folder=EBWebView/{Path.GetFileName(directory)} {result}");
        }
    }

    private static (int Locked, int Total, double Ms) LockedFiles(string root)
    {
        var started = Stopwatch.GetTimestamp();
        int locked = 0, total = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            total++;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                locked++;
            }
        }

        return (locked, total, Log.Ms(started));
    }

    // ---- Shutdown ------------------------------------------------------------------------------------------------

    private void CloseController()
    {
        if (_controller is not null)
        {
            _controller.Close();
            _controller = null;
        }
    }

    private async Task ShutdownAsync()
    {
        _engine?.Dispose();
        CloseController();
        if (_options.Has("wait-exit"))
        {
            await Task.WhenAny(_browserExited.Task, Task.Delay(20000));
        }

        PInvoke.DestroyWindow(_window);
        PInvoke.PostQuitMessage(0);
    }

    // ---- Window and assets ---------------------------------------------------------------------------------------

    private unsafe void CreateWindows()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparResourceUsageSpike")
        fixed (char* title = "Resource usage spike")
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
            _ui = new UiContext(_messageWindow);

            // Offscreen and never activated: runs must not take focus or show anything on the user's desktop.
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, title,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 480, 360, HWND.Null, HMENU.Null, instance, null);
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }
    }

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
                    return default;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var path = new Uri(args.GetRequest().GetUri()).AbsolutePath;
        var key = "www/" + path.TrimStart('/');
        using var stream = typeof(Host).Assembly.GetManifestResourceStream(key);
        if (stream is null)
        {
            args.PutResponse(Respond([], 404, "Not Found", "text/plain"));
            return;
        }

        var content = new byte[stream.Length];
        stream.ReadExactly(content);
        var type = Path.GetExtension(key) switch
        {
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => "text/html; charset=utf-8",
        };
        args.PutResponse(Respond(content, 200, "OK", type));
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType)
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        try
        {
            return _environment.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: no-store");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }
}

/// <summary>Every user-data folder of this spike lives under artifacts/webview2-profiles/resource-usage/.</summary>
internal static class Paths
{
    private static readonly Lazy<string> s_repository = new(() =>
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "spikes")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run from inside the repository: profiles go under artifacts/webview2-profiles/.");
    });

    public static string Root => Path.Combine(s_repository.Value, "artifacts", "webview2-profiles", "resource-usage");

    public static string Udf(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? Path.Combine(Root, name)
            : throw new ArgumentException("UDF names are ASCII letters, digits, '-' and '_'.", nameof(name));
}
