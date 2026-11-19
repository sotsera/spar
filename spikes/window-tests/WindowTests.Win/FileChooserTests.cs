using System.Diagnostics;
using System.Text.Json;

namespace Spar.WindowTests;

/// <summary>
/// Question 3: blocking the native file chooser with <c>Page.setInterceptFileChooserDialog</c>. Every click is a
/// trusted CDP click. Native dialogs are spotted by enumerating the top-level windows of this process and of the
/// folder's browser processes; a dialog that appears is closed at once with <c>WM_CLOSE</c> (it belongs to a process
/// this spike started), never clicked.
/// </summary>
internal static class FileChooserTests
{
    private const string Page = "https://app.localhost/file.html";
    private const int WatchMilliseconds = 1500;

    private static readonly List<string> s_chooserEvents = [];

    public static async Task<int> RunAsync(Options options)
    {
        var root = Paths.Udf(options.Get("root") ?? "filechooser");
        var only = options.Get("only");
        var env = await Env.CreateAsync(Path.Combine(root, "udf"));
        Log.Write("START", $"pid={Environment.ProcessId} only={only ?? "all"}");

        if (only is null or "control")
        {
            await ControlAsync(env);
        }

        if (only is null or "intercept")
        {
            await InterceptAsync(env, "intercept-no-page-enable", pageEnable: false, devTools: true, clicks: 3);
            await InterceptAsync(env, "intercept", pageEnable: true, devTools: true, clicks: 10);
            await InterceptAsync(env, "intercept-devtools-off", pageEnable: true, devTools: false, clicks: 10);
        }

        if (only is null or "race")
        {
            await RaceAsync(env);
        }

        if (only is null or "frames")
        {
            await FramesAsync(env);
        }

        if (only is null or "fsa")
        {
            await FileSystemAccessAsync(env, "fsa-default", null);
            await FileSystemAccessAsync(env, "fsa-permissions-policy", "Permissions-Policy: file-system-access=(), local-fs=()");
            await FileSystemAccessAsync(env, "fsa-csp-sandbox", "Content-Security-Policy: sandbox allow-scripts allow-same-origin");
        }

        if (only is null or "fsa-args")
        {
            string[] candidates = ["--disable-blink-features=FileSystemAccessLocal", "--disable-blink-features=FileSystemAccess", "--disable-features=FileSystemAccessAPI"];
            for (var index = 0; index < candidates.Length; index++)
            {
                var arguments = candidates[index];
                var argEnv = await Env.CreateAsync(Path.Combine(root, "args-" + index), arguments);
                await FileSystemAccessAsync(argEnv, "fsa-args " + arguments, null);
                await ProfileTests.WaitForNoProcessesAsync(argEnv.Path, 15000);
            }
        }

        await ProfileTests.WaitForNoProcessesAsync(env.Path, 15000);
        Log.Write("CLEANUP", $"deleted={Paths.TryDelete(root, out var error)} {error}");
        return 0;
    }

    /// <summary>No interception: the native dialog should open. It is closed with WM_CLOSE as soon as it is seen.</summary>
    private static async Task ControlAsync(Env env)
    {
        var view = await View.CreateAsync(env, "default");
        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();
        for (var run = 0; run < 3; run++)
        {
            var dialog = await ClickAndWatchAsync(view, "#f");
            await Task.Delay(300);
            Log.Write("CONTROL", $"run={run} dialog={dialog} page={await Events(view)}");
        }

        await view.ClickAsync("#pick");
        var picked = await WatchAsync(view.Env, Stopwatch.GetTimestamp());
        Log.Write("CONTROL", $"run=showPicker dialog={picked} page={await Events(view)}");
        view.Close();
    }

    private static async Task InterceptAsync(Env env, string label, bool pageEnable, bool devTools, int clicks)
    {
        var view = await View.CreateAsync(env, "default", devTools, beforeFirstUse: async v =>
        {
            Subscribe(v);
            if (pageEnable)
            {
                await v.CallAsync("Page.enable");
            }

            Log.Write("ENABLE", $"label={label} result={await v.CallAsync("Page.setInterceptFileChooserDialog", """{"enabled":true}""")}");
        });
        Log.Write("SETTINGS", $"label={label} areDevToolsEnabled={view.WebView.GetSettings().GetAreDevToolsEnabled()}");
        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();

        // Ten clicks on the same input, without answering the intercepted chooser.
        for (var run = 0; run < clicks; run++)
        {
            var dialog = await ClickAndWatchAsync(view, "#f", run < 3 ? WatchMilliseconds : 300);
            Log.Write("INTERCEPT", $"label={label} run={run} dialog={dialog} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");
        }

        if (!pageEnable)
        {
            view.Close();
            return;
        }

        // Answer one: DOM.setFileInputFiles with no files (the host's "cancel").
        var answered = await ClickAndWatchAsync(view, "#f", 300);
        var opened = TakeChooserEvents();
        var backend = BackendNodeId(opened);
        var answer = backend is null ? "no-event" : await TryCallAsync(view, "DOM.setFileInputFiles", $$"""{"files":[],"backendNodeId":{{backend}}}""");
        await Task.Delay(200);
        Log.Write("INTERCEPT", $"label={label} run=answer-empty dialog={answered} cdpEvents=[{opened}] answer={answer} page={await Events(view)}");

        // showPicker on the same input.
        await view.ClickAsync("#pick");
        Log.Write("INTERCEPT", $"label={label} run=showPicker dialog={await WatchAsync(env, Stopwatch.GetTimestamp())} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");

        // After a new document (same origin), a reload, and a cross-origin navigation.
        foreach (var step in new[] { "same-origin", "reload", "cross-origin", "back-to-app" })
        {
            switch (step)
            {
                case "same-origin":
                    await view.NavigateAsync(Page + "?second");
                    break;
                case "reload":
                    await view.ReloadAsync();
                    break;
                case "cross-origin":
                    await view.NavigateAsync("https://other.invalid/file.html");
                    break;
                default:
                    await view.NavigateAsync(Page + "?third");
                    break;
            }

            await view.TwoFramesAsync();
            var dialog = await ClickAndWatchAsync(view, "#f");
            Log.Write("INTERCEPT", $"label={label} run={step} dialog={dialog} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");
        }

        view.Close();
    }

    /// <summary>When must interception be on? The enable call is not awaited before the navigation and the click.</summary>
    private static async Task RaceAsync(Env env)
    {
        for (var run = 0; run < 5; run++)
        {
            Task<string>? enable = null;
            var view = await View.CreateAsync(env, "~private", beforeFirstUse: v =>
            {
                Subscribe(v);
                _ = v.CallAsync("Page.enable");
                enable = v.CallAsync("Page.setInterceptFileChooserDialog", """{"enabled":true}""");
                return Task.CompletedTask;
            });
            var enableDoneBeforeNavigate = enable!.IsCompleted;
            await view.NavigateAsync(Page);
            var enableDoneAfterNavigate = enable.IsCompleted;
            var dialog = await ClickAndWatchAsync(view, "#f");
            Log.Write("RACE", $"run={run} enableDoneBeforeNavigate={enableDoneBeforeNavigate} enableDoneAfterNavigate={enableDoneAfterNavigate} " +
                $"dialog={dialog} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");
            view.Close();
        }

        // A second controller on the same environment, never enabled: is interception per WebView?
        var other = await View.CreateAsync(env, "default");
        await other.NavigateAsync(Page);
        await other.TwoFramesAsync();
        Log.Write("RACE", $"run=second-webview-not-enabled dialog={await ClickAndWatchAsync(other, "#f")} page={await Events(other)}");
        other.Close();
    }

    /// <summary>A file input in a same-origin iframe and in a cross-site (out-of-process) iframe.</summary>
    private static async Task FramesAsync(Env env)
    {
        var attached = new List<string>();
        View? viewRef = null;
        var autoAttach = false;
        var view = await View.CreateAsync(env, "default", beforeFirstUse: async v =>
        {
            viewRef = v;
            Subscribe(v);
            v.Subscribe("Target.attachedToTarget", (json, session) =>
            {
                using var document = JsonDocument.Parse(json);
                var child = document.RootElement.GetProperty("sessionId").GetString()!;
                var type = document.RootElement.GetProperty("targetInfo").GetProperty("type").GetString();
                attached.Add($"{type}:{child[..8]}");
                if (autoAttach)
                {
                    _ = EnableChildAsync(viewRef!, child, attached);
                }
            });
            await v.CallAsync("Page.enable");
            await v.CallAsync("Page.setInterceptFileChooserDialog", """{"enabled":true}""");
        });

        foreach (var frame in new[] { "https://app.localhost/inner.html", "https://frame.invalid/inner.html" })
        {
            await view.NavigateAsync(Page + "?frame=" + Uri.EscapeDataString(frame));
            await Task.Delay(500);
            await view.TwoFramesAsync();
            var (x, y) = await view.CentreAsync("#frame", "#frame");
            var started = Stopwatch.GetTimestamp();
            await view.ClickAtAsync(x, y);
            var dialog = await WatchAsync(env, started);
            await Task.Delay(300);
            Log.Write("FRAME", $"frame={frame} autoAttach=false dialog={dialog} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");
        }

        // Auto-attach to child targets (flat sessions) and enable interception in each.
        autoAttach = true;
        Log.Write("AUTO-ATTACH", await view.CallAsync("Target.setAutoAttach", """{"autoAttach":true,"waitForDebuggerOnStart":true,"flatten":true}"""));
        foreach (var frame in new[] { "https://frame.invalid/inner.html", "https://frame.invalid/inner.html?again" })
        {
            await view.NavigateAsync(Page + "?frame=" + Uri.EscapeDataString(frame));
            await Task.Delay(500);
            await view.TwoFramesAsync();
            var (x, y) = await view.CentreAsync("#frame", "#frame");
            var started = Stopwatch.GetTimestamp();
            await view.ClickAtAsync(x, y);
            var dialog = await WatchAsync(env, started);
            await Task.Delay(300);
            Log.Write("FRAME", $"frame={frame} autoAttach=true attached=[{string.Join(',', attached)}] dialog={dialog} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");
            attached.Clear();
        }

        view.Close();
    }

    private static async Task EnableChildAsync(View view, string session, List<string> attached)
    {
        try
        {
            await view.CallForSessionAsync(session, "Page.enable");
            await view.CallForSessionAsync(session, "Page.setInterceptFileChooserDialog", """{"enabled":true}""");
            await view.CallForSessionAsync(session, "Runtime.runIfWaitingForDebugger");
            attached.Add("enabled:" + session[..8]);
        }
        catch (Exception exception)
        {
            attached.Add("failed:" + session[..8] + ":" + exception.Message.Split(' ')[^1]);
        }
    }

    /// <summary>File System Access pickers after trusted clicks, with interception on.</summary>
    private static async Task FileSystemAccessAsync(Env env, string label, string? header)
    {
        var view = await View.CreateAsync(env, "~private", beforeFirstUse: async v =>
        {
            Subscribe(v);
            await v.CallAsync("Page.enable");
            await v.CallAsync("Page.setInterceptFileChooserDialog", """{"enabled":true}""");
        });
        if (header is not null)
        {
            view.ExtraHeaders["/file.html"] = header;
        }

        await view.NavigateAsync(Page);
        await view.TwoFramesAsync();
        Log.Write("FSA", $"label={label} apis={await view.EvaluateTextAsync("JSON.stringify(apis())", awaitPromise: false)}");
        foreach (var button in new[] { "#open", "#dir", "#save", "#f" })
        {
            var dialog = await ClickAndWatchAsync(view, button);
            await Task.Delay(300);
            Log.Write("FSA", $"label={label} button={button} dialog={dialog} cdpEvents=[{TakeChooserEvents()}] page={await Events(view)}");
        }

        view.Close();
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------

    private static void Subscribe(View view) =>
        view.Subscribe("Page.fileChooserOpened", (json, session) =>
        {
            lock (s_chooserEvents)
            {
                s_chooserEvents.Add((session.Length > 0 ? "session=" + session[..8] + " " : "") + json);
            }
        });

    private static string TakeChooserEvents()
    {
        lock (s_chooserEvents)
        {
            var text = string.Join(" | ", s_chooserEvents);
            s_chooserEvents.Clear();
            return text;
        }
    }

    private static string? BackendNodeId(string events)
    {
        var index = events.IndexOf("\"backendNodeId\":", StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + 16;
        var end = start;
        while (end < events.Length && char.IsAsciiDigit(events[end]))
        {
            end++;
        }

        return events[start..end];
    }

    private static async Task<string> TryCallAsync(View view, string method, string parameters)
    {
        try
        {
            return await view.CallAsync(method, parameters);
        }
        catch (Exception exception)
        {
            return "error:" + exception.Message;
        }
    }

    private static async Task<string> ClickAndWatchAsync(View view, string selector, int watchMilliseconds = WatchMilliseconds)
    {
        var (x, y) = await view.CentreAsync(selector);
        var started = Stopwatch.GetTimestamp();
        await view.ClickAtAsync(x, y);
        return await WatchAsync(view.Env, started, watchMilliseconds);
    }

    /// <summary>
    /// Watches for a new visible top-level window of this process or the folder's browser processes (other than the
    /// spike's own test windows). A window that appears is closed with WM_CLOSE at once, then waited for.
    /// </summary>
    private static async Task<string> WatchAsync(Env env, long started, int watchMilliseconds = WatchMilliseconds)
    {
        var processes = ProcessTools.Find(Paths.Marker(env.Path), keepHandles: false).Select(p => p.Id).ToHashSet();
        processes.Add(Environment.ProcessId);
        while (Time.Ms(started) < watchMilliseconds)
        {
            var found = Native.VisibleWindowsOf(processes).Where(w => w.ClassName != "SparWindowTestsSpike").ToList();
            if (found.Count > 0)
            {
                var seenMs = Time.Ms(started);
                var description = string.Join(';', found.Select(w => $"{w.ClassName}/\"{w.Title}\"/pid={(w.ProcessId == Environment.ProcessId ? "host" : processes.Contains(w.ProcessId) ? "browser" : "other")}"));
                foreach (var window in found)
                {
                    Native.PostClose(window.Handle);
                }

                var closeStart = Stopwatch.GetTimestamp();
                while (Time.Ms(closeStart) < 3000 && Native.VisibleWindowsOf(processes).Any(w => w.ClassName != "SparWindowTestsSpike"))
                {
                    await Task.Delay(10);
                }

                var stillOpen = Native.VisibleWindowsOf(processes).Any(w => w.ClassName != "SparWindowTestsSpike");
                return $"OPENED({description} after={seenMs:F0}ms closedAfter={Time.Ms(closeStart):F0}ms stillOpen={stillOpen})";
            }

            await Task.Delay(10);
        }

        return "none";
    }

    private static Task<string> Events(View view) =>
        view.EvaluateTextAsync("(() => { const e = JSON.stringify(window.events.map(({t, ...rest}) => rest)); window.events.length = 0; return e; })()", awaitPromise: false);
}
