using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Spar.WebViewProfiles;

/// <summary>
/// The Linux app under test: GTK and WebKit on one thread with a GLib main loop and a GLib synchronization context.
/// Windows are given as "--windows a,a,b,~ephemeral" where each name is a data set (its own data and cache folders) and
/// "~ephemeral[:key]" an ephemeral one (views with the same key share it). On WebKitGTK 6.0 a data set is a
/// WebKitNetworkSession and all views share one WebKitWebContext; on 4.1 it is a WebKitWebsiteDataManager with its own
/// WebKitWebContext. The page reports through the script message handler "spar". Lines on stdout are
/// <c>KEY ft=&lt;CLOCK_MONOTONIC ns&gt; k=v ...</c>.
/// </summary>
internal sealed class LinuxHost
{
    private const string Origin = "spar://app";
    private static LinuxHost? s_host;

    private readonly Options _options;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly List<View> _views = [];
    private readonly ConcurrentQueue<string> _stdin = new();
    private TaskCompletionSource? _stdinSignal;
    private GLibContext? _ui;
    private nint _loop;
    private int _exitCode;
#if SPAR_GTK4
    private nint _context;
#endif

    private LinuxHost(Options options) => _options = options;

    public static string Api =>
#if SPAR_GTK4
        "gtk4";
#else
        "gtk3";
#endif

    public static long Now => Stopwatch.GetTimestamp();

    public static int Run(Options options)
    {
        var host = new LinuxHost(options);
        var thread = new Thread(host.RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        Log("HOST-EXIT", $"code={host._exitCode}");
        return host._exitCode;
    }

    public static void Log(string key, string text)
    {
        Console.Out.WriteLine($"{key} ft={Now} {text}");
        Console.Out.Flush();
    }

    private static double Ms(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private void RunOnUiThread()
    {
        s_host = this;
#if SPAR_GTK4
        if (Gtk.gtk_init_check() == 0)
#else
        if (Gtk.gtk_init_check(0, 0) == 0)
#endif
        {
            Log("HOST-FAILURE", "gtk_init_check failed (no display)");
            _exitCode = 5;
            return;
        }

        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(_options.Int("timeout", 90)));
            Log("TIMEOUT", "");
            Environment.Exit(3);
        }) { IsBackground = true };
        watchdog.Start();
        if (_options.Has("handshake"))
        {
            new Thread(() =>
            {
                while (Console.In.ReadLine() is { } line)
                {
                    _stdin.Enqueue(line);
                    _ui.Post(_ => _stdinSignal?.TrySetResult(), null);
                }

                _stdin.Enqueue("eof");
                _ui.Post(_ => _stdinSignal?.TrySetResult(), null);
            }) { IsBackground = true }.Start();
        }

        _ui.Post(static async state => await ((LinuxHost)state!).MainAsync(), this);
        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);
        if (_options.Get("exit") == "wait")
        {
            WaitForChildren();
        }
    }

    private async Task MainAsync()
    {
        try
        {
            var scenario = _options.Get("scenario") ?? "shutdown";
            switch (scenario)
            {
                case "shutdown":
                    StartViews();
                    if (_options.Get("page") != "none")
                    {
                        await _views[0].NavigateAsync(_options.Get("page") ?? "op=read");
                    }

                    break;
                case "memory":
                    StartViews();
                    foreach (var view in _views)
                    {
                        await view.NavigateAsync("op=read");
                    }

                    await Task.Delay(_options.Int("settle", 3000));
                    SampleMemory();
                    break;
                case "multi":
                    StartViews();
                    await MultiAsync();
                    break;
                case "clear":
                    StartViews();
                    await ClearAsync();
                    break;
                case "tests":
                    await TestsAsync();
                    break;
                case "scan":
                    Log("TOKEN-SCAN", $"label=offline home=[{string.Join(';', Scan.FilesContaining(Environment.GetEnvironmentVariable("HOME")!, _options.Get("token")!))}] " +
                        $"profiles=[{string.Join(';', Scan.FilesContaining(Paths.UdfRoot, _options.Get("token")!))}]");
                    break;
                default:
                    throw new InvalidOperationException("Unknown scenario " + scenario);
            }

            Log("READY", $"views={_views.Count}");
            if (_options.Has("handshake"))
            {
                // Commands before "go": "nav QUERY" navigates view 0 and logs its result.
                while (true)
                {
                    var line = await NextLineAsync();
                    if (line.StartsWith("nav ", StringComparison.Ordinal))
                    {
                        await _views[0].NavigateAsync(line[4..]);
                        Log("NAV-DONE", "");
                        continue;
                    }

                    Log("GO", $"line={line}");
                    break;
                }
            }
            else if (_options.Int("stay", 0) is var stay and > 0)
            {
                await Task.Delay(stay);
            }
        }
        catch (Exception exception)
        {
            Log("HOST-FAILURE", exception.ToString().ReplaceLineEndings(" | "));
            _exitCode = 2;
        }

        Shutdown();
    }

    private async Task<string> NextLineAsync()
    {
        while (true)
        {
            if (_stdin.TryDequeue(out var line))
            {
                return line;
            }

            _stdinSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_stdin.IsEmpty)
            {
                await _stdinSignal.Task;
            }
        }
    }

    // ---- Data sets and views -------------------------------------------------------------------------------------

    private void StartViews()
    {
        foreach (var spec in (_options.Get("windows") ?? "main").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            _views.Add(CreateView(SessionFor(spec)));
        }
    }

    private Session SessionFor(string spec)
    {
        if (_sessions.TryGetValue(spec, out var existing))
        {
            return existing;
        }

        var started = Now;
        var ephemeral = spec.StartsWith("~ephemeral", StringComparison.Ordinal);
        var root = ephemeral ? "" : Paths.Udf($"linux-{Api}-{spec}");
        var data = Path.Combine(root, "data");
        var cache = Path.Combine(root, "cache");
        var session = new Session(spec, ephemeral ? null : root);
#if SPAR_GTK4
        if (_context == 0)
        {
            _context = WebKit.webkit_web_context_new();
            RegisterScheme(_context);
        }

        session.Context = _context;
        session.Network = ephemeral ? WebKit.webkit_network_session_new_ephemeral() : WebKit.webkit_network_session_new(data, cache);
        session.Manager = WebKit.webkit_network_session_get_website_data_manager(session.Network);
#else
        session.Manager = ephemeral
            ? WebKit.webkit_website_data_manager_new_ephemeral()
            : GObject.New(WebKit.webkit_website_data_manager_get_type(), new Property("base-data-directory", data), new Property("base-cache-directory", cache));
        session.Context = WebKit.webkit_web_context_new_with_website_data_manager(session.Manager);
        WebKit.webkit_web_context_set_sandbox_enabled(session.Context, _options.Get("sandbox") == "off" ? 0 : 1);
        RegisterScheme(session.Context);
#endif
        Log("SESSION", $"name={spec} ephemeral={WebKit.webkit_website_data_manager_is_ephemeral(session.Manager)} " +
            $"data={Marshal.PtrToStringUTF8(WebKit.webkit_website_data_manager_get_base_data_directory(session.Manager)) ?? "-"} ms={Ms(started):F1}");
        _sessions[spec] = session;
        return session;
    }

    private static unsafe void RegisterScheme(nint context)
    {
        WebKit.webkit_web_context_register_uri_scheme(context, "spar", &OnSchemeRequest, 0, 0);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(WebKit.webkit_web_context_get_security_manager(context), "spar");
    }

    private unsafe View CreateView(Session session)
    {
        var started = Now;
        var view = new View(this, _views.Count, session);
#if SPAR_GTK4
        view.Window = Gtk.gtk_window_new();
#else
        view.Window = Gtk.gtk_window_new(0);
#endif
        Gtk.gtk_window_set_title(view.Window, "Spar profiles " + view.Index);
        Gtk.gtk_window_set_default_size(view.Window, 480, 360);
#if SPAR_GTK4
        view.WebView = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new Property("web-context", null, session.Context, WebKit.webkit_web_context_get_type()),
            new Property("network-session", null, session.Network, WebKit.webkit_network_session_get_type()));
#else
        view.WebView = GObject.New(WebKit.webkit_web_view_get_type(), new Property("web-context", null, session.Context, WebKit.webkit_web_context_get_type()));
#endif
        GObject.g_object_ref_sink(view.WebView);
        var manager = WebKit.webkit_web_view_get_user_content_manager(view.WebView);
#if SPAR_GTK4
        WebKit.webkit_user_content_manager_register_script_message_handler(manager, "spar", null);
#else
        WebKit.webkit_user_content_manager_register_script_message_handler(manager, "spar");
#endif
        GObject.Connect(manager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, view.Index);
#if SPAR_GTK4
        Gtk.gtk_window_set_child(view.Window, view.WebView);
#else
        Gtk.gtk_container_add(view.Window, view.WebView);
#endif
        // The plan's start-up order: attach the view and start loading before the window is shown (see Show()).
        view.Shown = false;
        Log("VIEW", $"view={view.Index} session={session.Name} ms={Ms(started):F1}");
        return view;
    }

    // ---- Scenarios -----------------------------------------------------------------------------------------------

    private void SampleMemory()
    {
        var self = Environment.ProcessId;
        var children = Proc.Descendants(self);
        var hostPss = Proc.PssKb(self);
        var byName = new SortedDictionary<string, (int Count, long Kb)>();
        long childPss = 0;
        foreach (var child in children)
        {
            var kb = Proc.PssKb(child.Pid);
            childPss += kb;
            var entry = byName.GetValueOrDefault(child.Name);
            byName[child.Name] = (entry.Count + 1, entry.Kb + kb);
        }

        Log("MEM", $"views={_views.Count} sessions={_sessions.Count} totalPssMB={(hostPss + childPss) / 1024.0:F1} hostPssMB={hostPss / 1024.0:F1} childPssMB={childPss / 1024.0:F1} " +
            string.Join(' ', byName.Select(e => $"{e.Key}={e.Value.Count}x{e.Value.Kb / 1024.0:F1}")));
    }

    private async Task MultiAsync()
    {
        foreach (var view in _views)
        {
            await view.NavigateAsync("op=read");
        }

        await _views[0].NavigateAsync("op=readwrite&token=W0");
        foreach (var view in _views.Skip(1))
        {
            await view.NavigateAsync("op=read");
        }

        if (_views.FirstOrDefault(v => v.Session.Root is null) is { } ephemeral)
        {
            await ephemeral.NavigateAsync("op=readwrite&token=EPHx7q9&bulk=512");
            await Task.Delay(1500);
            foreach (var view in _views.Where(v => v != ephemeral))
            {
                await view.NavigateAsync("op=read");
            }

            Log("TOKEN-SCAN", $"label=ephemeral-running home=[{string.Join(';', Scan.FilesContaining(Environment.GetEnvironmentVariable("HOME")!, "EPHx7q9"))}] " +
                $"profiles=[{string.Join(';', Scan.FilesContaining(Paths.UdfRoot, "EPHx7q9"))}]");

            // Ephemeral lifetime: a second view on a new ephemeral data set, and (if any) another view on the same one.
            var fresh = CreateView(SessionFor("~ephemeral:second"));
            _views.Add(fresh);
            fresh.Show();
            await fresh.NavigateAsync("op=read");
        }

        foreach (var session in _sessions.Values.Where(s => s.Root is not null))
        {
            var (files, bytes) = Paths.Size(session.Root!);
            Log("SIZE", $"session={session.Name} files={files} kB={bytes / 1024}");
        }
    }

    private async Task ClearAsync()
    {
        var view = _views[0];
        await view.NavigateAsync(_options.Get("page") ?? "op=readwrite&token=CLRz8w3&bulk=4096");
        await Task.Delay(_options.Int("settle", 2000));
        var root = view.Session.Root!;
        LogSize("before", root);
        var types = _options.Get("clear") is { } text && text != "all" ? int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : WebKit.DataAll;
        var started = Now;
        var ok = await ClearDataAsync(view.Session.Manager, types);
        Log("CLEARED", $"types=0x{types:X} ok={ok} ms={Ms(started):F1}");
        LogSize("after-clear", root);
        await Task.Delay(2000);
        LogSize("after-clear+2s", root);
        await view.NavigateAsync("op=read");
        Log("FILES", $"label=after-clear {Paths.Listing(root, 30)}");
        Log("TOKEN-SCAN", $"label=after-clear files=[{string.Join(';', Scan.FilesContaining(root, "CLRz8w3"))}]");
    }

    private void LogSize(string label, string path)
    {
        var (files, bytes) = Paths.Size(path);
        Log("SIZE", $"label={label} files={files} kB={bytes / 1024}");
    }

    private static readonly Dictionary<nint, TaskCompletionSource<bool>> s_clears = [];
    private static nint s_clearId;

    private static unsafe Task<bool> ClearDataAsync(nint manager, int types)
    {
        var id = ++s_clearId;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        s_clears[id] = completion;
        WebKit.webkit_website_data_manager_clear(manager, types, 0, 0, &OnCleared, id);
        return completion.Task;
    }

    private async Task TestsAsync()
    {
        // In-process "tests", each needing a clean data set: fresh folders, an ephemeral data set, or one shared
        // data set cleared between tests.
        var mode = _options.Get("mode") ?? "ephemeral";
        var count = _options.Int("count", 10);
        for (var i = 0; i < count; i++)
        {
            var begin = Now;
            var spec = mode switch
            {
                "freshdirs" => $"t{_runId}{i}",
                "ephemeral" => $"~ephemeral:t{i}",
                _ => "tshared",
            };
            var session = SessionFor(spec);
            var sessionMs = Ms(begin);
            var view = CreateView(session);
            _views.Add(view);
            view.Show();
            var pageStart = Now;
            var result = await view.NavigateAsync($"op=readwrite&token=T{i}");
            var pageMs = Ms(pageStart);
            var fresh = result.Contains("\"ls\":null", StringComparison.Ordinal) && result.Contains("\"idb\":null", StringComparison.Ordinal);
            var cleanupStart = Now;
            var cleanup = "";
            if (mode == "shared-clear")
            {
                var ok = await ClearDataAsync(session.Manager, WebKit.DataAll);
                cleanup = $"cleared={ok} ";
            }

            view.Destroy();
            if (mode != "shared-clear")
            {
                session.Release();
                _sessions.Remove(spec);
            }

            if (mode == "freshdirs")
            {
                // The folders can go only once no process holds files in them.
                var gone = await WaitNoUsersAsync(session.Root!, 10000);
                var deleteStart = Now;
                cleanup += $"no-users={gone} waitMs={Ms(cleanupStart):F1} deleted={gone && Paths.TryDelete(session.Root!)} deleteMs={Ms(deleteStart):F1} ";
            }

            Log("TEST", $"mode={mode} i={i} fresh={fresh} sessionMs={sessionMs:F1} pageMs={pageMs:F1} {cleanup}cleanupMs={Ms(cleanupStart):F1} totalMs={Ms(begin):F1} children={Proc.Descendants(Environment.ProcessId).Count}");
        }
    }

    private static async Task<bool> WaitNoUsersAsync(string root, int timeoutMilliseconds)
    {
        var started = Now;
        while (Ms(started) < timeoutMilliseconds)
        {
            if (Proc.FileUsers(root).Count == 0)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    // ---- Shutdown ------------------------------------------------------------------------------------------------

    private void Shutdown()
    {
        Log("CLOSE-START", $"views={_views.Count}");
        foreach (var view in _views)
        {
            view.Destroy();
        }

        foreach (var session in _sessions.Values)
        {
            session.Release();
        }

#if SPAR_GTK4
        if (_context != 0)
        {
            GObject.g_object_unref(_context);
            _context = 0;
        }
#endif
        Log("CLOSE-END", "");
        GLib.g_main_loop_quit(_loop);
    }

    private void WaitForChildren()
    {
        var started = Now;
        while (Ms(started) < 20000)
        {
            if (Proc.Descendants(Environment.ProcessId).Count == 0)
            {
                Log("CHILDREN-EXITED", $"ms={Ms(started):F1}");
                return;
            }

            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(2);
        }

        Log("CHILDREN-ALIVE", string.Join(' ', Proc.Descendants(Environment.ProcessId).Select(c => $"{c.Name}:{c.Pid}")));
    }

    // ---- Callbacks -----------------------------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint message, nint data)
    {
#if SPAR_GTK4
        var value = message;
#else
        var value = WebKit.webkit_javascript_result_get_js_value(message);
#endif
        var text = WebKit.jsc_value_to_string(value);
        var body = Marshal.PtrToStringUTF8(text) ?? "";
        GLib.g_free(text);
        if (s_host is { } host && (int)data < host._views.Count)
        {
            host._views[(int)data].Complete(body);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnCleared(nint source, nint result, nint data)
    {
        var ok = WebKit.webkit_website_data_manager_clear_finish(source, result, out var error) != 0;
        if (error != 0)
        {
            GLib.g_error_free(error);
        }

        if (s_clears.Remove(data, out var completion))
        {
            completion.TrySetResult(ok);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            Serve(request);
        }
        catch (Exception exception)
        {
            Log("SCHEME-FAILURE", exception.Message);
        }
    }

    private static unsafe void Serve(nint request)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var root = Path.Combine(Paths.Repository, "spikes", "webview-profiles", "www") + "/";
        byte[] content;
        string type;
        if (path == "/cacheable.txt")
        {
            content = "cacheable"u8.ToArray();
            type = "text/plain";
        }
        else
        {
            var file = Path.GetFullPath(Path.Combine(root, path == "/" ? "index.html" : path.TrimStart('/')));
            var found = file.StartsWith(root, StringComparison.Ordinal) && File.Exists(file);
            content = found ? File.ReadAllBytes(file) : "not found"u8.ToArray();
            type = !found ? "text/plain" : file.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : "text/html";
        }

        fixed (byte* bytes = content)
        {
            var gbytes = GLib.g_bytes_new(bytes, (nuint)content.Length);
            var stream = Gio.g_memory_input_stream_new_from_bytes(gbytes);
            WebKit.webkit_uri_scheme_request_finish(request, stream, content.Length, type);
            GObject.g_object_unref(stream);
            GLib.g_bytes_unref(gbytes);
        }
    }

    private sealed class Session(string name, string? root)
    {
        public string Name { get; } = name;

        /// <summary>The data set's folder (data/ and cache/ inside), or null when ephemeral.</summary>
        public string? Root { get; } = root;

        public nint Context { get; set; }

        public nint Network { get; set; }

        public nint Manager { get; set; }

        public void Release()
        {
#if SPAR_GTK4
            if (Network != 0)
            {
                GObject.g_object_unref(Network);
                Network = 0;
            }
#else
            if (Context != 0)
            {
                GObject.g_object_unref(Context);
                GObject.g_object_unref(Manager);
                Context = 0;
            }
#endif
        }
    }

    private sealed class View(LinuxHost host, int index, Session session)
    {
        private TaskCompletionSource<string>? _result;

        public int Index { get; } = index;

        public Session Session { get; } = session;

        public nint Window { get; set; }

        public nint WebView { get; set; }

        public bool Shown { get; set; }

        public void Show()
        {
            if (Shown)
            {
                return;
            }

            Shown = true;
#if SPAR_GTK4
            Gtk.gtk_window_present(Window);
#else
            Gtk.gtk_widget_show_all(Window);
#endif
        }

        public async Task<string> NavigateAsync(string query)
        {
            _result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = Now;
            WebKit.webkit_web_view_load_uri(WebView, $"{Origin}/?{query}&w={Index}");
            Show();
            var result = await _result.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Log("RESULT", $"view={Index} session={Session.Name} ms={Ms(started):F1} json={result}");
            _ = host;
            return result;
        }

        public void Complete(string body) => _result?.TrySetResult(body);

        public void Destroy()
        {
            if (Window == 0)
            {
                return;
            }

#if SPAR_GTK4
            Gtk.gtk_window_destroy(Window);
#else
            Gtk.gtk_widget_destroy(Window);
#endif
            GObject.g_object_unref(WebView);
            Window = 0;
            WebView = 0;
        }
    }
}
