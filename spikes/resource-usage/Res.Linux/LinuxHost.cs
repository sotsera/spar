using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Spar.ResourceUsage;

/// <summary>
/// The Linux app under test: GTK 4 and WebKitGTK 6.0 on one thread with a GLib main loop and a GLib synchronization
/// context. One window, one view, one network session with its own data and cache folders. Pages come from the spar://
/// scheme (www/) or, for the disk scenario, from the loopback server. The page reports through the "spar" script
/// message handler. Every measurement is a log line on stdout.
/// </summary>
internal sealed class LinuxHost
{
    private const string Origin = "spar://app";
    private static LinuxHost? s_host;
    private static TaskCompletionSource<(nint List, nint Error)>? s_fetch;

    private readonly Options _options;
    private readonly string _root;
    private readonly string _www;
    private GLibContext? _ui;
    private nint _loop;
    private nint _context;
    private nint _session;
    private nint _manager;
    private nint _window;
    private nint _view;
    private TaskCompletionSource<string>? _message;
    private int _exitCode;
    private readonly List<double> _pings = [];

    private LinuxHost(Options options)
    {
        _options = options;
        var repository = Repository();
        _root = Path.Combine(repository, "artifacts", "resource-usage", "linux", options.Get("data") ?? "default");
        _www = Path.Combine(repository, "spikes", "resource-usage", "www") + "/";
    }

    public static int Run(Options options)
    {
        var host = new LinuxHost(options);
        var thread = new Thread(host.RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        Log.Line("HOST-EXIT", $"code={host._exitCode}");
        return host._exitCode;
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "spikes")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run from inside the repository copy.");
    }

    private void RunOnUiThread()
    {
        s_host = this;
        if (Gtk.gtk_init_check() == 0)
        {
            Log.Line("HOST-FAILURE", "gtk_init_check failed (no display)");
            _exitCode = 5;
            return;
        }

        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        var timeout = _options.Int("timeout", 180);
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(timeout));
            Log.Line("TIMEOUT", "");
            Environment.Exit(3);
        }) { IsBackground = true }.Start();
        _ui.Post(static async state => await ((LinuxHost)state!).MainAsync(), this);
        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);
    }

    private async Task MainAsync()
    {
        try
        {
            Start();
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

    private unsafe void Start()
    {
        var started = Stopwatch.GetTimestamp();
        _context = WebKit.webkit_web_context_new();
        WebKit.webkit_web_context_register_uri_scheme(_context, "spar", &OnSchemeRequest, 0, 0);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(WebKit.webkit_web_context_get_security_manager(_context), "spar");
        _session = WebKit.webkit_network_session_new(Path.Combine(_root, "data"), Path.Combine(_root, "cache"));
        _manager = WebKit.webkit_network_session_get_website_data_manager(_session);
        _window = Gtk.gtk_window_new();
        Gtk.gtk_window_set_title(_window, "Spar resource usage");
        Gtk.gtk_window_set_default_size(_window, 480, 360);
        _view = GObject.New(WebKit.webkit_web_view_get_type(),
            ("web-context", _context, WebKit.webkit_web_context_get_type()),
            ("network-session", _session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_ref_sink(_view);
        var manager = WebKit.webkit_web_view_get_user_content_manager(_view);
        WebKit.webkit_user_content_manager_register_script_message_handler(manager, "spar", null);
        GObject.Connect(manager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        Gtk.gtk_window_set_child(_window, _view);
        Gtk.gtk_window_present(_window);
        Log.Line("START", $"ms={Log.Ms(started):F1} data={_root} pid={Environment.ProcessId}");
    }

    private async Task<string> NavigateAsync(string uri)
    {
        _message = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Stopwatch.GetTimestamp();
        WebKit.webkit_web_view_load_uri(_view, uri);
        var text = await _message.Task.WaitAsync(TimeSpan.FromSeconds(120));
        Log.Line("PAGE", $"uri={uri} ms={Log.Ms(started):F1} message={text}");
        return text;
    }

    // ---- Memory --------------------------------------------------------------------------------------------------

    private async Task MemoryAsync()
    {
        var iterations = _options.Int("iterations", 200);
        var self = Environment.ProcessId;

        // 1. Which processes: the app's tree, two ways, and the sandbox's PID namespaces.
        var scanned = ProcMem.DescendantsByScan(self);
        var children = ProcMem.DescendantsByChildren(self);
        Log.Line("PROCESSES", "source=proc-scan " + string.Join(' ', scanned.Select(c => $"{c.Name}:{c.Pid}<{c.Parent}")));
        Log.Line("PROCESSES", "source=task-children " + string.Join(' ', children.Select(c => $"{ProcMem.Name(c.Pid)}:{c.Pid}<{c.Parent}")));
        Log.Line("PROCESSES", $"source=compare scanCount={scanned.Count} childrenCount={children.Count} same={scanned.Select(c => c.Pid).Order().SequenceEqual(children.Select(c => c.Pid).Order())} " +
            $"allProcesses={Directory.EnumerateDirectories("/proc").Count(d => char.IsAsciiDigit(Path.GetFileName(d)[0]))}");
        foreach (var child in scanned)
        {
            Log.Line("NSPID", $"pid={child.Pid} name={child.Name} nspid={ProcMem.NsPid(child.Pid)} rollupReadable={ProcMem.ReadRollup(child.Pid) is not null}");
        }

        Log.Line("COST", $"what=descendants-by-proc-scan thread=main {Stats.Time(50, () => ProcMem.DescendantsByScan(self))}");
        Log.Line("COST", $"what=descendants-by-task-children thread=main {Stats.Time(iterations, () => ProcMem.DescendantsByChildren(self))}");

        // 2. Each candidate measure, per process.
        LogProcessMemory("loaded", scanned);

        // 3. Cost of one sample.
        var pids = scanned.Select(c => c.Pid).ToArray();
        Log.Line("COST", $"what=smaps_rollup-all thread=main {Stats.Time(iterations, () => ReadRollups(pids))}");
        Log.Line("COST", $"what=smaps_rollup-all thread=pool {await Task.Run(() => Stats.Time(iterations, () => ReadRollups(pids)))}");
        Log.Line("COST", $"what=statm-all thread=pool {await Task.Run(() => Stats.Time(iterations, () => { foreach (var pid in pids) { ProcMem.ReadStatm(pid); } }))}");
        Log.Line("COST", $"what=children+smaps_rollup-all thread=pool {await Task.Run(() => Stats.Time(iterations, () => FullSample(self)))}");
        Log.Line("COST", $"what=scan+smaps_rollup-all thread=pool {await Task.Run(() => Stats.Time(50, () => ReadRollups([.. ProcMem.DescendantsByScan(self).Select(c => c.Pid)])))}");
        Log.Line("COST", $"what=smaps_rollup-self thread=pool {await Task.Run(() => Stats.Time(iterations, () => ProcMem.ReadRollup(0)))}");
        Log.Line("COST", $"what=Process.GetProcessById.PrivateMemorySize64-all thread=pool {await Task.Run(() => Stats.Time(50, () =>
        {
            foreach (var pid in pids)
            {
                using var process = Process.GetProcessById(pid);
                _ = process.PrivateMemorySize64;
            }
        }))}");

        using var engine = new LinuxEngine(self);
        engine.Refresh();
        var fresh = ReadRollups(pids);
        var kept = engine.ReadRollup();
        Log.Line("PREAD-CHECK", $"freshPssKB={fresh} keptHandlePssKB={kept.Pss / 1024} keptPrivateDirtyKB={kept.PrivateDirty / 1024} statmAnonKB={engine.ReadStatm().Private / 1024} processes={engine.Count}");
        Log.Line("COST", $"what=kept-handles-statm-all thread=pool {await Task.Run(() => Stats.Time(iterations, () => engine.ReadStatm()))}");
        Log.Line("COST", $"what=kept-handles-smaps_rollup-all thread=pool {await Task.Run(() => Stats.Time(iterations, () => engine.ReadRollup()))}");
        Log.Line("COST", $"what=refresh-tree-and-open-handles thread=pool {await Task.Run(() => Stats.Time(50, engine.Refresh))}");

        // 4. What the main loop sees: dispatch latency of a ping posted about every millisecond.
        await PingPhaseAsync("idle", 3000, null);
        await PingPhaseAsync("sample-on-main-every-100ms", 3000, () => FullSample(self), onMain: true);
        await PingPhaseAsync("sample-on-pool-every-100ms", 3000, () => FullSample(self), onMain: false);

        // 5. The backend's own memory.
        Backend.Report("host", 20,
        [
            ("rollupPss", () => (ProcMem.ReadRollup(0)?.Pss ?? 0) * 1024),
            ("rollupUss", () => (ProcMem.ReadRollup(0)?.Uss ?? 0) * 1024),
            ("rollupPrivateDirty", () => (ProcMem.ReadRollup(0)?.PrivateDirty ?? 0) * 1024),
            ("statmResident", () => (ProcMem.ReadStatm(self)?.ResidentKb ?? 0) * 1024),
        ]);

        // 6. The sketch sampler.
        var every = new LatestValueSampler<MemoryReading>(_ => ValueTask.FromResult(Reading(self)), (_, _) => true, TimeSpan.FromMilliseconds(100));
        await Task.Run(() => LatestValueSampler<MemoryReading>.MeasureAsync("engine-every-tick", every, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300)));
        var keptEvery = new LatestValueSampler<MemoryReading>(_ => ValueTask.FromResult(engine.ReadStatm()), (_, _) => true, TimeSpan.FromMilliseconds(100));
        await Task.Run(() => LatestValueSampler<MemoryReading>.MeasureAsync("engine-kept-statm-every-tick", keptEvery, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300)));
        var threshold = new LatestValueSampler<MemoryReading>(
            _ => ValueTask.FromResult(Reading(self)),
            (a, b) => LatestValueSampler<MemoryReading>.Beyond(a.Private, b.Private, 0.01, 1 << 20) || a.Processes != b.Processes,
            TimeSpan.FromMilliseconds(100));
        await Task.Run(() => LatestValueSampler<MemoryReading>.MeasureAsync("engine-threshold-1%-1MB", threshold, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300)));
        LogProcessMemory("end", ProcMem.DescendantsByScan(self));
    }

    private static long ReadRollups(int[] pids)
    {
        long total = 0;
        foreach (var pid in pids)
        {
            total += ProcMem.ReadRollup(pid)?.Pss ?? 0;
        }

        return total;
    }

    private static long FullSample(int self)
    {
        long total = 0;
        foreach (var child in ProcMem.DescendantsByChildren(self))
        {
            total += ProcMem.ReadRollup(child.Pid)?.Pss ?? 0;
        }

        return total;
    }

    /// <summary>The web engine's memory: PSS and USS over the app's descendants (the app process itself excluded).</summary>
    private static MemoryReading Reading(int self)
    {
        long pss = 0, rss = 0;
        var count = 0;
        foreach (var child in ProcMem.DescendantsByChildren(self))
        {
            if (ProcMem.ReadRollup(child.Pid) is { } rollup)
            {
                pss += rollup.Pss * 1024;
                rss += rollup.Rss * 1024;
                count++;
            }
        }

        return new MemoryReading(pss, rss, count, Stopwatch.GetTimestamp());
    }

    private static void LogProcessMemory(string label, List<ProcMem.Child> children)
    {
        long rss = 0, pss = 0, uss = 0, privateDirty = 0, pssAnon = 0, pssFile = 0, pssShmem = 0, statmAnon = 0;
        foreach (var child in children)
        {
            var rollup = ProcMem.ReadRollup(child.Pid);
            var statm = ProcMem.ReadStatm(child.Pid);
            statmAnon += (statm?.ResidentKb ?? 0) - (statm?.SharedKb ?? 0);
            if (rollup is { } r)
            {
                rss += r.Rss;
                pss += r.Pss;
                uss += r.Uss;
                privateDirty += r.PrivateDirty;
                pssAnon += r.PssAnon;
                pssFile += r.PssFile;
                pssShmem += r.PssShmem;
            }

            Log.Line("MEM", string.Create(CultureInfo.InvariantCulture,
                $"label={label} pid={child.Pid} name={child.Name} readable={rollup is not null} rssKB={rollup?.Rss} pssKB={rollup?.Pss} pssAnonKB={rollup?.PssAnon} " +
                $"pssFileKB={rollup?.PssFile} pssShmemKB={rollup?.PssShmem} sharedCleanKB={rollup?.SharedClean} sharedDirtyKB={rollup?.SharedDirty} " +
                $"privateCleanKB={rollup?.PrivateClean} privateDirtyKB={rollup?.PrivateDirty} ussKB={rollup?.Uss} swapKB={rollup?.Swap} " +
                $"statmResidentKB={statm?.ResidentKb} statmSharedKB={statm?.SharedKb} statmAnonKB={statm?.ResidentKb - statm?.SharedKb}"));
        }

        var self = ProcMem.ReadRollup(0)!.Value;
        var selfStatm = ProcMem.ReadStatm(Environment.ProcessId);
        Log.Line("MEM-TOTAL", $"label={label} processes={children.Count} rssMB={Log.Mb(rss * 1024)} pssMB={Log.Mb(pss * 1024)} ussMB={Log.Mb(uss * 1024)} " +
            $"privateDirtyMB={Log.Mb(privateDirty * 1024)} pssAnonMB={Log.Mb(pssAnon * 1024)} pssFileMB={Log.Mb(pssFile * 1024)} pssShmemMB={Log.Mb(pssShmem * 1024)} " +
            $"hostRssMB={Log.Mb(self.Rss * 1024)} hostPssMB={Log.Mb(self.Pss * 1024)} hostUssMB={Log.Mb(self.Uss * 1024)} hostPrivateDirtyMB={Log.Mb(self.PrivateDirty * 1024)} " +
            $"statmAnonMB={Log.Mb(statmAnon * 1024)} hostStatmAnonMB={Log.Mb(((selfStatm?.ResidentKb ?? 0) - (selfStatm?.SharedKb ?? 0)) * 1024)}");
    }

    /// <summary>Same probe as the Windows host: a pool thread pings the main loop about every millisecond.</summary>
    private async Task PingPhaseAsync(string label, int milliseconds, Action? sample, bool onMain = false)
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
            async Task Loop()
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

            await (onMain ? Loop() : Task.Run(Loop));
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

    // ---- Disk ----------------------------------------------------------------------------------------------------

    private async Task DiskAsync()
    {
        var server = _options.Get("server") ?? throw new InvalidOperationException("--server is required for the disk scenario.");
        var fill = _options.Get("fill") ?? "ls=1024&idb=8&cache=4&http=40&size=100";
        await NavigateAsync($"{server}/fill.html?{fill}");
        await Task.Delay(_options.Int("settle", 3000));
        var iterations = _options.Int("iterations", 10);
        await Task.Run(() => WalkAll("running", iterations));
        await FetchAsync("running", iterations);
        await Task.Delay(_options.Int("later", 10000));
        await Task.Run(() => WalkAll("running-later", 3));
        await FetchAsync("running-later", 3);

        // After the view and session are gone and the children have exited.
        Gtk.gtk_window_destroy(_window);
        GObject.g_object_unref(_view);
        _window = 0;
        _view = 0;
        var started = Stopwatch.GetTimestamp();
        while (ProcMem.DescendantsByChildren(Environment.ProcessId).Count(c => ProcMem.Name(c.Pid).StartsWith("WebKit", StringComparison.Ordinal)) > 0 && Log.Ms(started) < 10000)
        {
            await Task.Delay(20);
        }

        Log.Line("VIEW-GONE", $"ms={Log.Ms(started):F1} webkitChildren={ProcMem.DescendantsByChildren(Environment.ProcessId).Count(c => ProcMem.Name(c.Pid).StartsWith("WebKit", StringComparison.Ordinal))}");
        await Task.Run(() => WalkAll("view-gone", 3));
    }

    private void WalkAll(string label, int iterations)
    {
        (string Name, string Path)[] targets = [("data", Path.Combine(_root, "data")), ("cache", Path.Combine(_root, "cache")), ("both", _root)];
        (string Name, Func<string, WalkResult> Walk)[] methods =
        [
            ("FileInfo", Walk.FileInfos),
            ("FileSystemEnumerator", Walk.Entries),
            ("lstat-blocks", LinuxWalk.Allocated),
        ];
        foreach (var (targetName, path) in targets)
        {
            foreach (var (methodName, walk) in methods)
            {
                var first = walk(path);
                var times = new List<double>();
                var last = first;
                for (var i = 0; i < iterations; i++)
                {
                    last = walk(path);
                    times.Add(last.Ms);
                }

                Log.Line("WALK", $"label={label} target={targetName} method={methodName} first=({first}) {last} repeat=({Stats.Of(times, "ms")})");
            }
        }

        foreach (var top in new[] { "data", "cache" })
        {
            var folder = Path.Combine(_root, top);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).Where(d => Path.GetRelativePath(folder, d).Count(c => c == '/') < 1))
            {
                Log.Line("WALK-PART", $"label={label} folder={top}/{Path.GetFileName(directory)} {LinuxWalk.Allocated(directory)}");
            }
        }
    }

    private async Task FetchAsync(string label, int iterations)
    {
        var times = new List<double>();
        var callTimes = new List<double>();
        for (var i = 0; i < iterations + 1; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var (list, error) = await FetchOnceAsync(callTimes);
            var elapsed = Log.Ms(started);
            var entries = new List<string>();
            ulong total = 0;
            for (var node = list; node != 0;)
            {
                var item = Marshal.PtrToStructure<GList>(node);
                var name = Marshal.PtrToStringUTF8(WebKit.webkit_website_data_get_name(item.Data));
                var types = WebKit.webkit_website_data_get_types(item.Data);
                var sizes = new List<string>();
                for (var bit = 0; bit < WebKit.DataTypeNames.Length; bit++)
                {
                    if ((types & (1 << bit)) != 0)
                    {
                        var size = WebKit.webkit_website_data_get_size(item.Data, 1 << bit);
                        total += size;
                        sizes.Add($"{WebKit.DataTypeNames[bit]}={size}");
                    }
                }

                entries.Add($"[{name}: {string.Join(',', sizes)}]");
                WebKit.webkit_website_data_unref(item.Data);
                node = item.Next;
            }

            GLib.g_list_free(list);
            if (error != 0)
            {
                GLib.g_error_free(error);
            }

            if (i == 0)
            {
                Log.Line("FETCH", $"label={label} first ms={elapsed:F2} entries={entries.Count} totalBytes={total} error={error != 0} {string.Join(' ', entries)}");
            }
            else
            {
                times.Add(elapsed);
            }
        }

        Log.Line("COST", $"what=website_data_manager_fetch(ALL)-to-callback label={label} {Stats.Of(times, "ms")} syncPartOnMain=({Stats.Of(callTimes, "ms")})");
    }

    private unsafe Task<(nint List, nint Error)> FetchOnceAsync(List<double> callTimes)
    {
        s_fetch = new TaskCompletionSource<(nint, nint)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Stopwatch.GetTimestamp();
        WebKit.webkit_website_data_manager_fetch(_manager, WebKit.DataAll, 0, &OnFetched, 0);
        callTimes.Add(Log.Ms(started));
        return s_fetch.Task;
    }

    [UnmanagedCallersOnly]
    private static void OnFetched(nint source, nint result, nint data)
    {
        var list = WebKit.webkit_website_data_manager_fetch_finish(source, result, out var error);
        s_fetch?.TrySetResult((list, error));
    }

    // ---- Shutdown ------------------------------------------------------------------------------------------------

    private async Task ShutdownAsync()
    {
        if (_window != 0)
        {
            Gtk.gtk_window_destroy(_window);
            GObject.g_object_unref(_view);
        }

        if (_session != 0)
        {
            GObject.g_object_unref(_session);
        }

        if (_context != 0)
        {
            GObject.g_object_unref(_context);
        }

        // Keep the loop running briefly so the children see the closed page, then quit.
        await Task.Delay(100);
        GLib.g_main_loop_quit(_loop);
    }

    // ---- Callbacks -----------------------------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint message, nint data)
    {
        var text = WebKit.jsc_value_to_string(message);
        var body = Marshal.PtrToStringUTF8(text) ?? "";
        GLib.g_free(text);
        s_host?._message?.TrySetResult(body);
    }

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            s_host!.Serve(request);
        }
        catch (Exception exception)
        {
            Log.Line("SCHEME-FAILURE", exception.Message);
        }
    }

    private unsafe void Serve(nint request)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var file = Path.GetFullPath(Path.Combine(_www, path.TrimStart('/')));
        var found = file.StartsWith(_www, StringComparison.Ordinal) && File.Exists(file);
        var content = found ? File.ReadAllBytes(file) : "not found"u8.ToArray();
        var type = !found ? "text/plain" : Path.GetExtension(file) switch { ".js" => "text/javascript", ".css" => "text/css", _ => "text/html" };
        fixed (byte* bytes = content)
        {
            var gbytes = GLib.g_bytes_new(bytes, (nuint)content.Length);
            var stream = Gio.g_memory_input_stream_new_from_bytes(gbytes);
            WebKit.webkit_uri_scheme_request_finish(request, stream, content.Length, type);
            GObject.g_object_unref(stream);
            GLib.g_bytes_unref(gbytes);
        }
    }
}

/// <summary>A du-like walk: lstat on every entry, st_size and st_blocks (512-byte units).</summary>
internal static unsafe class LinuxWalk
{
    public static WalkResult Allocated(string root)
    {
        var started = Stopwatch.GetTimestamp();
        long files = 0, directories = 0, bytes = 0, allocated = 0, errors = 0;
        var buffer = stackalloc byte[256];
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false });
                foreach (var entry in entries)
                {
                    if (Libc.lstat(entry, buffer) != 0)
                    {
                        errors++;
                        continue;
                    }

                    var mode = *(uint*)(buffer + 24) & 0xF000;
                    if (mode == 0x4000)
                    {
                        directories++;
                        allocated += *(long*)(buffer + 64) * 512;
                        pending.Push(entry);
                    }
                    else
                    {
                        files++;
                        bytes += *(long*)(buffer + 48);
                        allocated += *(long*)(buffer + 64) * 512;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors++;
            }
        }

        return new WalkResult(files, directories, bytes, allocated, errors, Log.Ms(started));
    }
}
