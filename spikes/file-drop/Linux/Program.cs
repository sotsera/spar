// Spike (M3c): how files dropped on a WebKitGTK 6.0 page reach the host with their paths, and what GtkFileDialog does
// without a file-chooser portal. Run in Spar's Linux CI image on a private Xvfb:
//   FileDrop.Linux page|motion|target <results.json> <fixtures folder>
//   FileDrop.Linux dialog <results.json>
// page: no host controller, only WebKit's own drop handling. motion: a GtkDropControllerMotion on the view reads the
// drop's file list itself while WebKit handles the drop. target: a GtkDropTarget for a file list on the view, in the
// capture phase. dialog: GtkFileDialog.open on the window, cancelled after 1.5 s unless it ends first.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FileDrop.Native;

namespace FileDrop;

internal sealed class Scenario(string name)
{
    public string Name { get; } = name;
    public TaskCompletionSource<JsonNode?> Report { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<JsonObject> HostFiles { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public JsonArray Navigations { get; } = [];
    public JsonArray HostEvents { get; } = [];
}

internal static unsafe class Program
{
    private const int HandledX = 150;
    private const int UnhandledX = 550;
    private const int AreaY = 250;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly JsonObject Results = [];
    private static readonly TaskCompletionSource<ulong> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string _mode = "page";
    private static nint _window;
    private static nint _loop;
    private static nint _readDrop;
    private static nint _dialogCancellable;
    private static double _dialogStarted;
    private static volatile Scenario? _current;

    private static int Main(string[] args)
    {
        _mode = args.Length > 0 ? args[0] : "page";
        var output = args.Length > 1 ? args[1] : "results.json";
        var fixtures = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "file-drop-fixtures");
        Results["mode"] = _mode;
        Results["sessionBus"] = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is null ? "none" : "present";

        XInitThreads();
        if (gtk_init_check() == 0)
        {
            Console.Error.WriteLine("gtk_init_check failed");
            return 2;
        }

        _loop = g_main_loop_new(0, 0);
        _window = gtk_window_new();
        gtk_window_set_decorated(_window, 0);
        gtk_window_set_default_size(_window, 800, 500);

        var view = webkit_web_view_new();
        var manager = webkit_web_view_get_user_content_manager(view);
        g_signal_connect_data(manager, "script-message-received::spar", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, 0, 0, 0);
        webkit_user_content_manager_register_script_message_handler(manager, "spar", 0);
        // A relay in an isolated world, as Spar's bridge has: does it see the page's drop, and can it tell a real one?
        const string relay = """
            window.addEventListener("drop", (e) => {
              window.webkit.messageHandlers.relay.postMessage(JSON.stringify({
                isTrusted: e.isTrusted, x: e.clientX, y: e.clientY,
                area: e.target && e.target.closest ? (e.target.closest("[id]") || {}).id || null : null,
                files: e.dataTransfer ? e.dataTransfer.files.length : -1,
              }));
            }, true);
            """;
        webkit_user_content_manager_add_script(manager, webkit_user_script_new_for_world(relay, 1, 0, "probe", 0, 0));
        g_signal_connect_data(manager, "script-message-received::relay", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnRelayMessage, 0, 0, 0);
        webkit_user_content_manager_register_script_message_handler(manager, "relay", "probe");
        Results["relay"] = new JsonArray();
        g_signal_connect_data(view, "decide-policy", (nint)(delegate* unmanaged<nint, nint, int, nint, int>)&OnDecidePolicy, 0, 0, 0);
        AddDropController(view);

        gtk_window_set_child(_window, view);
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "page.html"));
        webkit_web_view_load_html(view, html, "http://probe.local/");
        gtk_window_present(_window);

        var worker = new Thread(() => Drive(fixtures, output)) { IsBackground = true };
        worker.Start();
        g_main_loop_run(_loop);
        return 0;
    }

    private static void AddDropController(nint view)
    {
        if (_mode == "motion")
        {
            var motion = gtk_drop_controller_motion_new();
            g_signal_connect_data(motion, "enter", (nint)(delegate* unmanaged<nint, double, double, nint, void>)&OnMotionEnter, 0, 0, 0);
            gtk_widget_add_controller(view, motion);
        }
        else if (_mode is "target" or "target-pass")
        {
            var target = gtk_drop_target_new(gdk_file_list_get_type(), 1);
            gtk_event_controller_set_propagation_phase(target, 1);
            g_signal_connect_data(target, "drop", (nint)(delegate* unmanaged<nint, nint, double, double, nint, int>)&OnTargetDrop, 0, 0, 0);
            gtk_widget_add_controller(view, target);
        }
    }

    // The worker thread: waits for the page, then makes each drop through its own X connection.
    private static void Drive(string fixtures, string output)
    {
        try
        {
            var xid = Ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            if (_mode.StartsWith("dialog", StringComparison.Ordinal))
            {
                Thread.Sleep(3000);
            }
            else
            {
                RunDrops(xid, fixtures);
            }
        }
        catch (Exception error)
        {
            Results["error"] = error.ToString();
        }

        File.WriteAllText(output, Results.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        g_main_loop_quit(_loop);
    }

    private static void RunDrops(ulong xid, string fixtures)
    {
        MakeFixtures(fixtures);
        var source = new XdndSource(Clock);
        var scenarios = new JsonArray();
        Results["scenarios"] = scenarios;
        var a = Path.Combine(fixtures, "a.txt");

        scenarios.Add(DropOne(source, xid, "one file, handled area", HandledX, [a]));
        scenarios.Add(DropOne(source, xid, "unicode name", HandledX, [Path.Combine(fixtures, "ünïcödé 文件.txt")]));
        scenarios.Add(DropOne(source, xid, "a file and a folder", HandledX, [a, Path.Combine(fixtures, "folder")]));
        scenarios.Add(DropOne(source, xid, "a 50 MB file", HandledX, [Path.Combine(fixtures, "big.bin")]));
        var many = Directory.GetFiles(Path.Combine(fixtures, "many")).Order(StringComparer.Ordinal).ToArray();
        scenarios.Add(DropOne(source, xid, "1,000 files", HandledX, many));
        scenarios.Add(DropOne(source, xid, "one file, area without a handler", UnhandledX, [a]));

        var pageTimes = new JsonArray();
        var hostTimes = new JsonArray();
        for (var i = 0; i < 11; i++)
        {
            var timed = DropOne(source, xid, "timed", HandledX, [a]);
            if (i > 0)
            {
                pageTimes.Add(timed["dropToPageMs"]?.DeepClone());
                hostTimes.Add(timed["dropToHostMs"]?.DeepClone());
            }
        }

        Results["oneFileDropToPageMs"] = pageTimes;
        Results["oneFileDropToHostMs"] = hostTimes;
    }

    private static JsonObject DropOne(XdndSource source, ulong xid, string name, int x, string[] paths)
    {
        var scenario = new Scenario(name);
        _current = scenario;
        var result = new JsonObject { ["name"] = name, ["paths"] = paths.Length };
        result["xdnd"] = source.Drop(xid, x, AreaY, paths);

        var report = scenario.Report.Task.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null).GetAwaiter().GetResult();
        result["pageReport"] = report?.DeepClone();
        if (report?["receivedAtMs"] is { } pageAt)
        {
            result["dropToPageMs"] = Math.Round(pageAt.GetValue<double>() - source.DropSentAt, 2);
        }

        if (_mode != "page")
        {
            var host = scenario.HostFiles.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null).GetAwaiter().GetResult();
            result["hostFiles"] = host?.DeepClone();
            if (host?["atMs"] is { } hostAt)
            {
                result["dropToHostMs"] = Math.Round(hostAt.GetValue<double>() - source.DropSentAt, 2);
            }
        }

        Thread.Sleep(200);
        result["navigations"] = scenario.Navigations.DeepClone();
        result["hostEvents"] = scenario.HostEvents.DeepClone();
        _current = null;
        return result;
    }

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint value, nint data)
    {
        try
        {
            var text = TakeUtf8(jsc_value_to_string(value)) ?? "null";
            var report = JsonNode.Parse(text)!.AsObject();
            if (report["kind"]?.GetValue<string>() == "ready")
            {
                var surface = gtk_native_get_surface(_window);
                Ready.TrySetResult(gdk_x11_surface_get_xid(surface));
                if (_mode.StartsWith("dialog", StringComparison.Ordinal))
                {
                    StartDialog();
                }

                return;
            }

            if (report["kind"]?.GetValue<string>() == "dragenter")
            {
                _current?.HostEvents.Add($"page dragenter at {Math.Round(Clock.Elapsed.TotalMilliseconds, 2)} ms");
                return;
            }

            report["receivedAtMs"] = Math.Round(Clock.Elapsed.TotalMilliseconds, 2);
            _current?.Report.TrySetResult(report);
        }
        catch (Exception error)
        {
            Results["callbackError"] = error.ToString();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnRelayMessage(nint manager, nint value, nint data)
    {
        var text = TakeUtf8(jsc_value_to_string(value)) ?? "null";
        var message = JsonNode.Parse(text)!.AsObject();
        message["atMs"] = Math.Round(Clock.Elapsed.TotalMilliseconds, 2);
        message["scenario"] = _current?.Name;
        Results["relay"]!.AsArray().Add(message);
    }

    [UnmanagedCallersOnly]
    private static int OnDecidePolicy(nint view, nint decision, int type, nint data)
    {
        if (type > 1)
        {
            return 0;
        }

        var action = webkit_navigation_policy_decision_get_navigation_action(decision);
        var uri = Utf8(webkit_uri_request_get_uri(webkit_navigation_action_get_request(action))) ?? "";
        if (uri.StartsWith("http://probe.local/", StringComparison.Ordinal))
        {
            return 0;
        }

        _current?.Navigations.Add(new JsonObject { ["uri"] = uri, ["newWindow"] = type == 1 });
        webkit_policy_decision_ignore(decision);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnMotionEnter(nint controller, double x, double y, nint data)
    {
        var drop = gtk_drop_controller_motion_get_drop(controller);
        _current?.HostEvents.Add($"motion enter at {Math.Round(Clock.Elapsed.TotalMilliseconds, 2)} ms, drop {(drop == 0 ? "none" : "present")}");
        if (drop == 0 || drop == _readDrop)
        {
            return;
        }

        _readDrop = drop;
        g_object_ref(drop);
        gdk_drop_read_value_async(drop, gdk_file_list_get_type(), 0, 0, &OnFileListRead, 0);
    }

    [UnmanagedCallersOnly]
    private static void OnFileListRead(nint drop, nint result, nint data)
    {
        nint error = 0;
        var value = gdk_drop_read_value_finish(drop, result, &error);
        var files = value == 0 ? new JsonObject { ["error"] = ErrorText(error) } : FileList(value);
        g_object_unref(drop);
        _current?.HostFiles.TrySetResult(files);
    }

    [UnmanagedCallersOnly]
    private static int OnTargetDrop(nint target, nint value, double x, double y, nint data)
    {
        var files = FileList(value);
        files["x"] = x;
        files["y"] = y;

        // target-pass declines drops on the right half, to see whether WebKit's own target then gets them.
        var taken = _mode != "target-pass" || x < 400;
        files["taken"] = taken;
        _current?.HostFiles.TrySetResult(files);
        return taken ? 1 : 0;
    }

    private static JsonObject FileList(nint value)
    {
        var at = Math.Round(Clock.Elapsed.TotalMilliseconds, 2);
        var list = gdk_file_list_get_files(g_value_get_boxed(value));
        var count = 0;
        var first = new JsonArray();
        for (var node = list; node != 0; node = *(nint*)(node + sizeof(nint)))
        {
            var path = TakeUtf8(g_file_get_path(*(nint*)node));
            if (count < 3)
            {
                first.Add(path);
            }

            count++;
        }

        g_slist_free(list);
        return new JsonObject { ["atMs"] = at, ["count"] = count, ["first"] = first };
    }

    private static void StartDialog()
    {
        var dialog = gtk_file_dialog_new();
        _dialogCancellable = g_cancellable_new();
        _dialogStarted = Clock.Elapsed.TotalMilliseconds;
        gtk_file_dialog_open(dialog, _window, _dialogCancellable, &OnDialogDone, 0);
        g_timeout_add(1500, &OnDialogTimeout, 0);
    }

    [UnmanagedCallersOnly]
    private static int OnDialogTimeout(nint data)
    {
        var toplevels = gtk_window_list_toplevels();
        var titles = new JsonArray();
        for (var node = toplevels; node != 0; node = *(nint*)(node + sizeof(nint)))
        {
            titles.Add(Utf8(gtk_window_get_title(*(nint*)node)) ?? "(no title)");
        }

        g_list_free(toplevels);
        Results["toplevelsAt1500Ms"] = titles;
        Results["cancelledAtMs"] = Math.Round(Clock.Elapsed.TotalMilliseconds - _dialogStarted, 2);
        g_cancellable_cancel(_dialogCancellable);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void OnDialogDone(nint dialog, nint result, nint data)
    {
        nint error = 0;
        var file = gtk_file_dialog_open_finish(dialog, result, &error);
        Results["dialogEndedAfterMs"] = Math.Round(Clock.Elapsed.TotalMilliseconds - _dialogStarted, 2);
        Results["dialogResult"] = file != 0 ? "a file" : ErrorText(error);
    }

    private static void MakeFixtures(string fixtures)
    {
        Directory.CreateDirectory(fixtures);
        File.WriteAllText(Path.Combine(fixtures, "a.txt"), "alpha\n");
        File.WriteAllText(Path.Combine(fixtures, "ünïcödé 文件.txt"), "unicode\n");
        var folder = Directory.CreateDirectory(Path.Combine(fixtures, "folder"));
        File.WriteAllText(Path.Combine(folder.FullName, "one.md"), "# one\n");
        File.WriteAllText(Path.Combine(folder.FullName, "two.md"), "# two\n");
        Directory.CreateDirectory(Path.Combine(folder.FullName, "inner"));
        using (var big = File.Create(Path.Combine(fixtures, "big.bin")))
        {
            big.SetLength(50L * 1024 * 1024);
        }

        var many = Directory.CreateDirectory(Path.Combine(fixtures, "many"));
        for (var i = 0; i < 1000; i++)
        {
            File.WriteAllText(Path.Combine(many.FullName, $"f{i:0000}.txt"), "x");
        }
    }
}
