// The Linux probe of spikes/webview-restart-wait: GTK4 + WebKitGTK 6.0 on one thread with the GLib main context pumped
// by the probe. Cycles 0..reps each create a window and a WebKitWebView, load spar://app/index.html (served from
// memory by a URI scheme handler, as Spar's host serves its page), optionally dwell, and destroy the window. Cycle 0 is
// the first view of the process; cycles 1..reps are the "next view after the last one closed". Every WebKit helper
// process descended from the probe (web, network, GPU, bwrap, the D-Bus proxy) is tracked through /proc every 5 ms,
// so each line can say which processes were new and when the previous view's processes exited after its close.
//   RestartWait.Linux --data <folder> --out <log> [--reps 10] [--gap 1000] [--dwell 0]
//     [--session default|ephemeral-new|ephemeral-reuse|persistent-new|persistent-reuse] [--window shown|hidden]
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RestartWait.Linux;

var options = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["--data"] = "", ["--out"] = "", ["--reps"] = "10", ["--gap"] = "1000", ["--dwell"] = "0", ["--session"] = "default", ["--window"] = "shown",
};
for (var i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i]] = args[i + 1];
}

return Probe.Run(options);

internal static unsafe class Probe
{
    private const string Page = "<!doctype html><title>restart</title><p>restart wait probe</p><script>fetch('spar://app/data').then(r => r.text()).catch(() => 0);</script>";

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Dictionary<int, Helper> Helpers = [];
    private static readonly int Self = Environment.ProcessId;
    private static StreamWriter s_log = StreamWriter.Null;
    private static double s_committed;
    private static double s_finished;
    private static double s_lastScan;

    private static double Now => Clock.Elapsed.TotalMilliseconds;

    public static int Run(Dictionary<string, string> options)
    {
        s_log = new StreamWriter(options["--out"], append: false) { AutoFlush = true };
        Note("options " + string.Join(" ", options.Where(o => o.Key is not "--data" and not "--out").Select(o => $"{o.Key} {o.Value}")));
        try
        {
            if (Gtk.gtk_init_check() == 0)
            {
                Note("FAILED gtk_init_check");
                return 1;
            }

            GLib.g_timeout_add_full(0, 5, &Tick, 0, 0);
            WebKit.webkit_web_context_register_uri_scheme(WebKit.webkit_web_context_get_default(), "spar", &Serve, 0, 0);
            Execute(options);
            return 0;
        }
        catch (Exception exception)
        {
            Note("FAILED " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
        finally
        {
            s_log.Dispose();
        }
    }

    private static void Execute(Dictionary<string, string> options)
    {
        var reps = int.Parse(options["--reps"]);
        var gap = int.Parse(options["--gap"]);
        var dwell = int.Parse(options["--dwell"]);
        var mode = options["--session"];
        var data = options["--data"];
        nint shared = mode switch
        {
            "ephemeral-reuse" => WebKit.webkit_network_session_new_ephemeral(),
            "persistent-reuse" => WebKit.webkit_network_session_new(Path.Combine(data, "session-data"), Path.Combine(data, "session-cache")),
            _ => 0,
        };
        var closedAt = 0.0;
        var createTimes = new List<double>();
        var finishTimes = new List<double>();
        HashSet<int> previous = [];
        for (var index = 0; index <= reps; index++)
        {
            if (index > 0)
            {
                Pump(() => false, gap);
            }

            Scan();
            var start = Now;
            var session = mode switch
            {
                "default" => WebKit.webkit_network_session_get_default(),
                "ephemeral-new" => WebKit.webkit_network_session_new_ephemeral(),
                "persistent-new" => WebKit.webkit_network_session_new(Path.Combine(data, "session-data"), Path.Combine(data, "session-cache")),
                _ => shared,
            };
            var window = Gtk.gtk_window_new();
            Gtk.gtk_window_set_default_size(window, 1024, 768);
            var view = GObject.NewWithObject(WebKit.webkit_web_view_get_type(), "network-session", WebKit.webkit_network_session_get_type(), session);
            GObject.g_signal_connect_data(view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, 0, 0, 0);
            GObject.g_signal_connect_data(view, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnTerminated, 0, 0, 0);
            Gtk.gtk_window_set_child(window, view);
            if (options["--window"] != "hidden")
            {
                Gtk.gtk_window_present(window);
            }

            s_committed = s_finished = 0;
            WebKit.webkit_web_view_load_uri(view, "spar://app/index.html");
            if (!Pump(() => s_finished > 0, 60_000))
            {
                throw new TimeoutException("The load did not finish in 60 s.");
            }

            var created = Helpers.Values.Where(h => h.Seen >= start).OrderBy(h => h.Seen).Select(h => $"{h.Kind}#{h.Pid} +{h.Seen - start:0}");
            var reused = Helpers.Values.Where(h => h.Seen < start && h.Gone == 0).Select(h => $"{h.Kind}#{h.Pid}");
            var line = $"cycle {index}: committed {s_committed - start:0} ms, finished {s_finished - start:0} ms; new [{string.Join(", ", created)}]; alive before [{string.Join(", ", reused)}]";
            if (index > 0)
            {
                line += $"; close->create {start - closedAt:0} ms; previous view's processes: " +
                    string.Join(", ", previous.Select(pid => Helpers[pid]).Select(h => $"{h.Kind}#{h.Pid} {(h.Gone > 0 ? $"exit +{h.Gone - closedAt:0}" : "alive")}"));
                createTimes.Add(s_finished - start);
            }

            Note(line);
            finishTimes.Add(s_finished - start);
            Pump(() => false, dwell);
            previous = [.. Helpers.Values.Where(h => h.Gone == 0).Select(h => h.Pid)];
            Gtk.gtk_window_destroy(window);
            closedAt = Now;
            if (mode is "ephemeral-new" or "persistent-new")
            {
                GObject.g_object_unref(session);
            }
        }

        Pump(() => false, 8_000);
        Note("after 8 s: " + string.Join(", ", Helpers.Values.OrderBy(h => h.Seen).Select(h => $"{h.Kind}#{h.Pid} seen {h.Seen:0} {(h.Gone > 0 ? $"gone {h.Gone:0} (+{h.Gone - closedAt:0} after last close)" : "alive")}")));
        if (shared != 0)
        {
            GObject.g_object_unref(shared);
        }

        createTimes.Sort();
        if (createTimes.Count > 0)
        {
            Note($"summary: first view {finishTimes[0]:0} ms; restarts n={createTimes.Count} to load finished p50 {createTimes[createTimes.Count / 2]:0} ms " +
                $"(min {createTimes[0]:0}, max {createTimes[^1]:0}), multi-second (>=2000 ms) {createTimes.Count(v => v >= 2000)}");
        }
    }

    /// <summary>Pumps the GLib main context until <paramref name="done"/> or the timeout (a 5 ms timer keeps it waking).</summary>
    private static bool Pump(Func<bool> done, double timeout)
    {
        var end = Now + timeout;
        while (!done())
        {
            if (Now >= end)
            {
                return false;
            }

            GLib.g_main_context_iteration(0, 1);
        }

        return true;
    }

    /// <summary>Every live descendant of the probe, from /proc; marks helpers seen and gone.</summary>
    private static HashSet<int> Scan()
    {
        s_lastScan = Now;
        var parents = new Dictionary<int, (int Parent, string Name)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var close = stat.LastIndexOf(')');
                var name = stat[(stat.IndexOf('(') + 1)..close];
                var parent = int.Parse(stat[(close + 2)..].Split(' ')[1]);
                parents[pid] = (parent, name);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var live = new HashSet<int>();
        foreach (var (pid, (_, name)) in parents)
        {
            if (pid != Self && (Helpers.ContainsKey(pid) || Descends(pid, parents)))
            {
                live.Add(pid);
                if (!Helpers.ContainsKey(pid))
                {
                    Helpers[pid] = new Helper(pid, Kind(name), s_lastScan);
                }
            }
        }

        foreach (var helper in Helpers.Values.Where(h => h.Gone == 0 && !live.Contains(h.Pid)))
        {
            helper.Gone = s_lastScan;
        }

        return live;
    }

    private static bool Descends(int pid, Dictionary<int, (int Parent, string Name)> parents)
    {
        for (var depth = 0; depth < 8 && parents.TryGetValue(pid, out var entry); depth++)
        {
            if (entry.Parent == Self)
            {
                return true;
            }

            pid = entry.Parent;
        }

        return false;
    }

    private static string Kind(string name) => name switch
    {
        "WebKitWebProces" => "web",
        "WebKitNetworkPr" => "network",
        "WebKitGPUProces" => "gpu",
        _ => name,
    };

    [UnmanagedCallersOnly]
    private static int Tick(nint data)
    {
        Scan();
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data)
    {
        if (loadEvent == WebKit.LoadCommitted)
        {
            s_committed = Now;
        }
        else if (loadEvent == WebKit.LoadFinished)
        {
            s_finished = Now;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnTerminated(nint view, int reason, nint data) => Note($"  web-process-terminated reason {reason}");

    [UnmanagedCallersOnly]
    private static void Serve(nint request, nint data)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "";
        var page = path.EndsWith("/index.html", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(page ? Page : new string('d', 4096));
        var stream = Gio.MemoryStream(bytes);
        WebKit.webkit_uri_scheme_request_finish(request, stream, bytes.Length, page ? "text/html" : "text/plain");
        GObject.g_object_unref(stream);
    }

    private static void Note(string text) => s_log.WriteLine($"{Now,10:0.0} | {text}");

    private sealed class Helper(int pid, string kind, double seen)
    {
        public int Pid { get; } = pid;

        public string Kind { get; } = kind;

        public double Seen { get; } = seen;

        public double Gone { get; set; }
    }
}
