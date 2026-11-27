// Spike (M3d): several WebKitGTK 6.0 windows of one app. One persistent network session, as an app has, and one user
// content manager per view, as Spar's host has. Measures: the time from the request to a new window's first frame;
// the web processes and their memory with 1, 2, 4 and 8 views on one origin; what webkit_web_view_terminate_web_process
// on one view does to the others; zoom per view, across a reload and a new view, and with Ctrl+wheel and Ctrl+plus
// through XTEST; WebKitFindController's counts; and a modal GTK 4 window: does its owner still take a click?
//   Windows.Linux <results.json> <data folder>
// Run on a private Xvfb, with or without a window manager (results.json names which).
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using static WindowsSpike.Native;

namespace WindowsSpike;

internal sealed class View(int index, string name)
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    public nint Window { get; set; }
    public nint WebView { get; set; }
    public TaskCompletionSource<double> Frame { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<JsonNode> Events { get; } = new();
    public TaskCompletionSource<string> Answer { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal static class Program
{
    private const uint CaseInsensitive = 1;
    private const uint WrapAround = 16;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly ConcurrentQueue<Action> Work = new();
    private static readonly List<View> Views = [];
    private static readonly ConcurrentQueue<JsonNode> FindEvents = new();
    private static readonly JsonObject Results = [];
    private static nint _loop;
    private static nint _session;
    private static string _html = "";

    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "results.json";
        var data = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "windows-spike");
        Results["windowManager"] = Environment.GetEnvironmentVariable("SPIKE_WM") ?? "none";
        _html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "page.html"));

        XInitThreads();
        if (gtk_init_check() == 0)
        {
            Console.Error.WriteLine("gtk_init_check failed");
            return 2;
        }

        _loop = g_main_loop_new(0, 0);
        _session = webkit_network_session_new(Path.Combine(data, "data"), Path.Combine(data, "cache"));
        var code = 1;
        var worker = new Thread(() =>
        {
            try
            {
                DriveAsync().GetAwaiter().GetResult();
                code = 0;
            }
            catch (Exception error)
            {
                Results["error"] = error.ToString();
            }

            var text = Results.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(output, text);
            Console.WriteLine(text);
            OnLoop(() => g_main_loop_quit(_loop));
        }) { IsBackground = true };
        worker.Start();
        g_main_loop_run(_loop);
        return code;
    }

    private static async Task DriveAsync()
    {
        var opened = new JsonArray();
        var processes = new JsonObject();
        Results["opened"] = opened;
        Results["processes"] = processes;
        processes["0"] = Processes();
        for (var i = 0; i < 8; i++)
        {
            opened.Add(await OpenAsync(i.ToString(), 800, 600));
            await Task.Delay(700);
            processes[(i + 1).ToString()] = Processes();
        }

        Results["find"] = await FindAsync(Views[0]);
        Results["zoom"] = await ZoomAsync();
        Results["terminate"] = await TerminateAsync(Views[3]);
        Results["afterTerminate"] = Processes();
        Results["modal"] = await ModalAsync();
    }

    // The loop's work queue: the worker posts, the loop runs.
    private static Task OnLoop(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Enqueue(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        });
        Post();
        return done.Task;
    }

    private static unsafe void Post() => g_idle_add(&Pump, 0);

    [UnmanagedCallersOnly]
    private static int Pump(nint data)
    {
        while (Work.TryDequeue(out var action))
        {
            action();
        }

        return 0;
    }

    private static double Now => Clock.Elapsed.TotalMilliseconds;

    /// <summary>A window with its own content manager and a view on the shared session; the time to its first frame.</summary>
    private static async Task<JsonObject> OpenAsync(string name, int width, int height)
    {
        var view = new View(Views.Count, name);
        Views.Add(view);
        var start = Now;
        double created = 0;
        await OnLoop(() => created = Create(view, width, height));

        var frame = await view.Frame.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return new JsonObject { ["window"] = name, ["createdMs"] = created - start, ["frameMs"] = frame - start };
    }

    /// <summary>The window, its content manager and view, then the load (loop thread); the time the view existed.</summary>
    private static unsafe double Create(View view, int width, int height)
    {
        view.Window = gtk_window_new();
        gtk_window_set_default_size(view.Window, width, height);
        gtk_window_set_title(view.Window, $"windows spike {view.Name}");
        var manager = webkit_user_content_manager_new();
        webkit_user_content_manager_register_script_message_handler(manager, "probe", 0);
        g_signal_connect_data(manager, "script-message-received::probe", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMessage, view.Index, 0, 0);
        view.WebView = NewView(_session, manager);
        g_object_unref(manager);
        g_signal_connect_data(view.WebView, "web-process-terminated", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnTerminated, view.Index, 0, 0);
        g_signal_connect_data(view.WebView, "notify::zoom-level", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnZoom, view.Index, 0, 0);
        gtk_window_set_child(view.Window, view.WebView);
        var created = Now;
        webkit_web_view_load_html(view.WebView, _html, $"https://app.probe/index.html?w={view.Name}");
        gtk_window_present(view.Window);
        return created;
    }

    private static unsafe nint NewView(nint session, nint content)
    {
        var values = stackalloc GValue[2];
        values[0] = default;
        values[1] = default;
        fixed (byte* sessionName = "network-session\0"u8)
        fixed (byte* contentName = "user-content-manager\0"u8)
        {
            var names = stackalloc nint[2] { (nint)sessionName, (nint)contentName };
            g_value_init(&values[0], webkit_network_session_get_type());
            g_value_set_object(&values[0], session);
            g_value_init(&values[1], webkit_user_content_manager_get_type());
            g_value_set_object(&values[1], content);
            var view = g_object_new_with_properties(webkit_web_view_get_type(), 2, names, values);
            g_value_unset(&values[0]);
            g_value_unset(&values[1]);
            return view;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnMessage(nint manager, nint value, nint data)
    {
        var view = Views[(int)data];
        var message = JsonNode.Parse(TakeString(jsc_value_to_string(value))!)!.AsObject();
        message["at"] = Now;
        if ((string?)message["type"] == "frame")
        {
            view.Frame.TrySetResult(Now);
        }

        view.Events.Enqueue(message);
    }

    [UnmanagedCallersOnly]
    private static void OnTerminated(nint webView, int reason, nint data) =>
        Views[(int)data].Events.Enqueue(new JsonObject { ["type"] = "terminated", ["reason"] = reason, ["at"] = Now });

    [UnmanagedCallersOnly]
    private static void OnZoom(nint webView, nint spec, nint data) =>
        Views[(int)data].Events.Enqueue(new JsonObject { ["type"] = "zoom", ["level"] = webkit_web_view_get_zoom_level(webView), ["at"] = Now });

    /// <summary>Our process's descendants by name, and their memory (resident minus shared, from statm).</summary>
    private static JsonObject Processes()
    {
        var parents = new Dictionary<int, int>();
        var names = new Dictionary<int, string>();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(folder), out var pid))
            {
                continue;
            }

            try
            {
                var stat = File.ReadAllText(Path.Combine(folder, "stat"));
                var close = stat.LastIndexOf(')');
                names[pid] = stat[(stat.IndexOf('(') + 1)..close];
                parents[pid] = int.Parse(stat[(close + 2)..].Split(' ')[1]);
            }
            catch (IOException)
            {
            }
        }

        var self = Environment.ProcessId;
        var result = new JsonObject();
        var counts = new Dictionary<string, int>();
        long bytes = Memory(self);
        foreach (var (pid, _) in parents)
        {
            var at = pid;
            var depth = 0;
            while (parents.TryGetValue(at, out var parent) && parent != 0 && depth++ < 20)
            {
                if (parent == self)
                {
                    var name = names[pid];
                    counts[name] = counts.GetValueOrDefault(name) + 1;
                    bytes += Memory(pid);
                    break;
                }

                at = parent;
            }
        }

        foreach (var (name, count) in counts)
        {
            result[name] = count;
        }

        result["memoryBytes"] = bytes;
        return result;
    }

    private static long Memory(int pid)
    {
        try
        {
            var fields = File.ReadAllText($"/proc/{pid}/statm").Split(' ');
            return (long.Parse(fields[1]) - long.Parse(fields[2])) * Environment.SystemPageSize;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static async Task<JsonObject> FindAsync(View view)
    {
        var result = new JsonObject();
        nint controller = 0;
        await OnLoop(() => controller = ConnectFind(view));

        async Task<JsonNode> StepAsync(string label, Action call)
        {
            FindEvents.Clear();
            var start = Now;
            await OnLoop(call);
            await Task.Delay(500);
            var events = new JsonArray();
            foreach (var e in FindEvents)
            {
                e["ms"] = (double)e["at"]! - start;
                events.Add(e.DeepClone());
            }

            return new JsonObject { ["step"] = label, ["events"] = events };
        }

        var steps = new JsonArray
        {
            await StepAsync("count apple", () => webkit_find_controller_count_matches(controller, "apple", CaseInsensitive, 100)),
            await StepAsync("search apple", () => webkit_find_controller_search(controller, "apple", CaseInsensitive | WrapAround, 100)),
            await StepAsync("next", () => webkit_find_controller_search_next(controller)),
            await StepAsync("search Row 1", () => webkit_find_controller_search(controller, "row 1", CaseInsensitive | WrapAround, 1000)),
            await StepAsync("search zzz", () => webkit_find_controller_search(controller, "zzz", CaseInsensitive | WrapAround, 100)),
            await StepAsync("finish", () => webkit_find_controller_search_finish(controller)),
        };
        result["steps"] = steps;
        return result;
    }

    private static unsafe nint ConnectFind(View view)
    {
        var controller = webkit_web_view_get_find_controller(view.WebView);
        g_signal_connect_data(controller, "counted-matches", (nint)(delegate* unmanaged<nint, uint, nint, void>)&OnCounted, 0, 0, 0);
        g_signal_connect_data(controller, "found-text", (nint)(delegate* unmanaged<nint, uint, nint, void>)&OnFound, 0, 0, 0);
        g_signal_connect_data(controller, "failed-to-find-text", (nint)(delegate* unmanaged<nint, nint, void>)&OnFailed, 0, 0, 0);
        return controller;
    }

    private static unsafe void Evaluate(View view, string script) =>
        webkit_web_view_evaluate_javascript(view.WebView, script, -1, 0, 0, 0, &OnEvaluated, view.Index);

    [UnmanagedCallersOnly]
    private static void OnCounted(nint controller, uint count, nint data) => FindEvents.Enqueue(new JsonObject { ["type"] = "counted-matches", ["count"] = count, ["at"] = Now });

    [UnmanagedCallersOnly]
    private static void OnFound(nint controller, uint count, nint data) => FindEvents.Enqueue(new JsonObject { ["type"] = "found-text", ["count"] = count, ["at"] = Now });

    [UnmanagedCallersOnly]
    private static void OnFailed(nint controller, nint data) => FindEvents.Enqueue(new JsonObject { ["type"] = "failed-to-find-text", ["at"] = Now });

    private static async Task<JsonObject> ZoomAsync()
    {
        var result = new JsonObject();
        var one = Views[1];
        await OnLoop(() => webkit_web_view_set_zoom_level(one.WebView, 1.5));
        await Task.Delay(300);
        result["afterSet"] = await LevelsAsync();

        one.Frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // A new document in the same view: reloading a load_html page refetches its base address, which no server answers.
        await OnLoop(() => webkit_web_view_load_html(one.WebView, _html, "https://app.probe/index.html?w=1"));
        await one.Frame.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(300);
        result["afterReload"] = (await LevelsAsync())["1"]?.DeepClone();

        result["newWindowOpen"] = await OpenAsync("zoom-new", 800, 600);
        await Task.Delay(500);
        var levels = await LevelsAsync();
        result["newWindow"] = levels["zoom-new"]?.DeepClone();

        // The newest window is on top, at the place the X server reports for it.
        var display = XOpenDisplay(0);
        var control = XKeysymToKeycode(display, 0xffe3);
        var over = await PointAsync(Views[^1], 300, 300);
        result["wheelPoint"] = $"{over.X},{over.Y}";
        XTestFakeMotionEvent(display, -1, over.X, over.Y, 0);
        XFlush(display);
        await Task.Delay(200);
        XTestFakeKeyEvent(display, control, 1, 0);
        XTestFakeButtonEvent(display, 4, 1, 0);
        XTestFakeButtonEvent(display, 4, 0, 0);
        XTestFakeKeyEvent(display, control, 0, 0);
        XFlush(display);
        await Task.Delay(700);
        result["afterCtrlWheel"] = (await LevelsAsync())["zoom-new"]?.DeepClone();

        var plus = XKeysymToKeycode(display, 0xffab);
        XTestFakeKeyEvent(display, control, 1, 0);
        XTestFakeKeyEvent(display, plus, 1, 0);
        XTestFakeKeyEvent(display, plus, 0, 0);
        XTestFakeKeyEvent(display, control, 0, 0);
        XFlush(display);
        await Task.Delay(700);
        result["afterCtrlPlus"] = (await LevelsAsync())["zoom-new"]?.DeepClone();
        result["zoomNewEvents"] = new JsonArray(Views[^1].Events.Where(e => (string?)e["type"] != "frame").Select(e => e.DeepClone()).ToArray());
        result["oneEvents"] = new JsonArray(one.Events.Where(e => (string?)e["type"] == "zoom").Select(e => e.DeepClone()).ToArray());
        return result;
    }

    private static async Task<JsonObject> LevelsAsync()
    {
        var levels = new JsonObject();
        await OnLoop(() =>
        {
            foreach (var view in Views)
            {
                levels[view.Name] = webkit_web_view_get_zoom_level(view.WebView);
            }
        });
        return levels;
    }

    /// <summary>Ends <paramref name="target"/>'s web process as Spar's "Reload page" does, then asks every page to answer.</summary>
    private static async Task<JsonObject> TerminateAsync(View target)
    {
        var result = new JsonObject();
        result["before"] = Processes();
        var start = Now;
        await OnLoop(() => webkit_web_view_terminate_web_process(target.WebView));
        await Task.Delay(2000);

        var answers = new JsonObject();
        foreach (var view in Views)
        {
            view.Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var asked = Now;
            await OnLoop(() => Evaluate(view, "document.getElementById('name').textContent"));
            var done = await Task.WhenAny(view.Answer.Task, Task.Delay(3000));
            var terminated = view.Events.Where(e => (string?)e["type"] == "terminated").Select(e =>
            {
                var copy = e.DeepClone();
                copy["at"] = (double)e["at"]! - start;
                return copy;
            }).ToArray();
            answers[view.Name] = new JsonObject
            {
                ["answer"] = done == view.Answer.Task ? view.Answer.Task.Result : "no answer in 3 s",
                ["ms"] = Now - asked,
                ["terminated"] = new JsonArray(terminated),
            };
        }

        result["views"] = answers;
        return result;
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnEvaluated(nint webView, nint result, nint data)
    {
        nint error = 0;
        var value = webkit_web_view_evaluate_javascript_finish(webView, result, &error);
        var view = Views[(int)data];
        if (value == 0)
        {
            view.Answer.TrySetResult("error");
            g_error_free(error);
            return;
        }

        view.Answer.TrySetResult(TakeString(jsc_value_to_string(value)) ?? "null");
        g_object_unref(value);
    }

    /// <summary>
    /// An owner window and a small window transient for it: a click on the owner's page where the small one does not
    /// cover it, first while the small one is not modal, then while it is; then a click on the small one.
    /// </summary>
    private static async Task<JsonObject> ModalAsync()
    {
        var result = new JsonObject();
        var owner = Views.Count;
        result["ownerOpen"] = await OpenAsync("owner", 800, 600);
        result["dialogOpen"] = await OpenAsync("dialog", 300, 200);
        var ownerView = Views[owner];
        var dialogView = Views[owner + 1];
        await OnLoop(() => gtk_window_set_transient_for(dialogView.Window, ownerView.Window));
        await Task.Delay(500);

        var display = XOpenDisplay(0);
        async Task<JsonNode> ClickAsync(string label, View expected, int x, int y)
        {
            var before = Now;
            XTestFakeMotionEvent(display, -1, x, y, 0);
            XFlush(display);
            await Task.Delay(150);
            XTestFakeButtonEvent(display, 1, 1, 0);
            XTestFakeButtonEvent(display, 1, 0, 0);
            XFlush(display);
            await Task.Delay(600);
            var got = new JsonObject();
            foreach (var view in new[] { ownerView, dialogView })
            {
                got[view.Name] = view.Events.Count(e => (string?)e["type"] == "click" && (double)e["at"]! > before);
            }

            return new JsonObject { ["step"] = label, ["at"] = $"{x},{y}", ["clicks"] = got };
        }

        // Points inside each window, from where the X server has it: without a window manager both sit at 0,0, the
        // newest on top, so (600, 450) is the owner's alone; openbox places them itself.
        var ownerPoint = await PointAsync(ownerView, 600, 450);
        var dialogPoint = await PointAsync(dialogView, 150, 100);
        result["ownerPoint"] = $"{ownerPoint.X},{ownerPoint.Y}";
        result["dialogPoint"] = $"{dialogPoint.X},{dialogPoint.Y}";

        var steps = new JsonArray
        {
            await ClickAsync("owner, dialog transient, not modal", ownerView, ownerPoint.X, ownerPoint.Y),
        };
        await OnLoop(() => gtk_window_set_modal(dialogView.Window, 1));
        await Task.Delay(300);
        steps.Add(await ClickAsync("owner, dialog modal", ownerView, ownerPoint.X, ownerPoint.Y));
        steps.Add(await ClickAsync("dialog, modal", dialogView, dialogPoint.X, dialogPoint.Y));
        await OnLoop(() => gtk_window_set_modal(dialogView.Window, 0));
        await Task.Delay(300);
        steps.Add(await ClickAsync("owner, dialog no longer modal", ownerView, ownerPoint.X, ownerPoint.Y));
        result["steps"] = steps;
        return result;
    }

    /// <summary>
    /// A point inside a view, in root coordinates: where the X server has the window's surface, plus the offset (the
    /// page's screenX and screenY were 0 under a window manager that had placed the window elsewhere).
    /// </summary>
    private static async Task<(int X, int Y)> PointAsync(View view, int x, int y)
    {
        ulong xid = 0;
        await OnLoop(() => xid = gdk_x11_surface_get_xid(gtk_native_get_surface(view.Window)));
        var display = XOpenDisplay(0);
        XTranslateCoordinates(display, xid, XDefaultRootWindow(display), x, y, out var rootX, out var rootY, out _);
        return (rootX, rootY);
    }
}
