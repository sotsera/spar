using System.Diagnostics;
using System.Runtime.InteropServices;
using Spar.WindowAutomation.Linux.Automation;
using Spar.WindowAutomation.Linux.Native;
using static Spar.WindowAutomation.Linux.Automation.Assertions;

namespace Spar.WindowAutomation.Linux;

internal static partial class Probes
{
    private const string Read = """
        (async () => {
          const db = await new Promise(r => { const q = indexedDB.open('spike'); q.onupgradeneeded = () => q.result.createObjectStore('s'); q.onsuccess = () => r(q.result); q.onerror = () => r(null); });
          let idb = '-';
          if (db) { idb = await new Promise(r => { const g = db.transaction('s').objectStore('s').get('k'); g.onsuccess = () => r(g.result ?? '-'); g.onerror = () => r('error'); }); db.close(); }
          return 'local=' + (localStorage.getItem('k') ?? '-') + ' session=' + (sessionStorage.getItem('k') ?? '-') + ' idb=' + idb + ' cookie=' + (document.cookie || '-');
        })()
        """;

    private static string Write(string value) => $$"""
        (async () => {
          localStorage.setItem('k', '{{value}}'); sessionStorage.setItem('k', '{{value}}'); document.cookie = 'c={{value}}; Secure; SameSite=Strict';
          const db = await new Promise(r => { const q = indexedDB.open('spike'); q.onupgradeneeded = () => q.result.createObjectStore('s'); q.onsuccess = () => r(q.result); });
          await new Promise(r => { const t = db.transaction('s', 'readwrite'); t.objectStore('s').put('{{value}}', 'k'); t.oncomplete = r; });
          db.close();
          return 'cookie readable right after writing it: ' + (document.cookie || '-');
        })()
        """;

    /// <summary>Per-test isolation with ephemeral network sessions, consecutive and simultaneous, and their costs.</summary>
    public static async Task<int> IsolationAsync(IInput input)
    {
        var created = new Timings();
        for (var test = 0; test < 5; test++)
        {
            var started = Stopwatch.GetTimestamp();
            var view = await created.TimeAsync("create (ephemeral session)", () => View.CreateAsync());
            await created.TimeAsync("first navigation", () => view.NavigateAsync(Session.Origin + "/"));
            var page = WindowPage.Attach(view, input);
            var found = (await page.EvaluateAsync(Read)).GetString();
            var wrote = (await page.EvaluateAsync(Write("test" + test))).GetString();
            var web = Processes.Count("WebKitWebProces");
            var network = Processes.Count("WebKitNetworkPr");
            await created.TimeAsync("close", view.CloseAsync);
            Log.Write("ISOLATION", $"consecutive test {test}: open+write+close={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms found=[{found}] {wrote}; processes while open: web={web} network={network}");
        }

        created.Report("ISOLATION-COST");
        var sessionCost = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var session = await Ui.Invoke(WebKit.webkit_network_session_new_ephemeral);
            sessionCost.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            await Ui.Invoke(() => GLib.g_object_unref(session));
        }

        Log.Write("ISOLATION-COST", $"webkit_network_session_new_ephemeral (round trip to the UI thread included) {Stats.Summary(sessionCost)}");

        // Two tests at the same time in one process: own sessions, then (control) one shared session.
        foreach (var shared in new[] { false, true })
        {
            var session = shared ? await Ui.Invoke(WebKit.webkit_network_session_new_ephemeral) : 0;
            var a = await View.CreateAsync(session);
            var b = await View.CreateAsync(session);
            await Task.WhenAll(a.NavigateAsync(Session.Origin + "/"), b.NavigateAsync(Session.Origin + "/"));
            var pageA = WindowPage.Attach(a, input);
            var pageB = WindowPage.Attach(b, input);
            await pageA.EvaluateAsync(Write("from-a"));
            var found = (await pageB.EvaluateAsync(Read)).GetString();
            Log.Write("ISOLATION", $"simultaneous, {(shared ? "one shared ephemeral session (control)" : "a session each")}: b found=[{found}]; processes: web={Processes.Count("WebKitWebProces")} network={Processes.Count("WebKitNetworkPr")}");
            await a.CloseAsync();
            await b.CloseAsync();
            if (session != 0)
            {
                await Ui.Invoke(() => GLib.g_object_unref(session));
            }
        }

        // Control: a persistent session on a folder keeps what the previous test wrote.
        var folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "/nonexistent", "persistent-control");
        for (var test = 0; test < 2; test++)
        {
            var session = await Ui.Invoke(() => WebKit.webkit_network_session_new(Path.Combine(folder, "data"), Path.Combine(folder, "cache")));
            var view = await View.CreateAsync(session);
            await Ui.Invoke(() => GLib.g_object_unref(session));
            await view.NavigateAsync(Session.Origin + "/");
            var page = WindowPage.Attach(view, input);
            var found = (await page.EvaluateAsync(Read)).GetString();
            await page.EvaluateAsync(Write("persistent" + test));
            await view.CloseAsync();
            await Task.Delay(500);
            Log.Write("ISOLATION", $"persistent session on one folder, test {test}: found=[{found}]");
        }

        return 0;
    }

    /// <summary>
    /// The engine's view of the page's accessibility tree in three states (orders page, menu open, dialog open),
    /// written as JSON; with --hold, each state waits for a marker file so an AT-SPI client can read the same state.
    /// </summary>
    public static async Task<int> TreeAsync(IInput input, string? hold, string output)
    {
        var view = await View.CreateAsync();
        var page = WindowPage.Attach(view, input);
        await view.NavigateAsync(Session.Origin + "/");
        await page.WaitForUiReadyAsync();
        await Expect(page.GetByRole("list", "Recent orders").GetByRole("listitem")).ToHaveCountAsync(3);
        await page.GetByLabel("Customer name").FillAsync("Ada Lovelace");
        async Task DumpAsync(string state)
        {
            await page.WaitForUiReadyAsync();
            var started = Stopwatch.GetTimestamp();
            var tree = await page.EngineAsync("""{"op":"tree"}""");
            var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            await File.WriteAllTextAsync(Path.Combine(output, $"tree-engine-{state}.json"), tree.GetRawText());
            Log.Write("TREE", $"{state}: {tree.GetArrayLength()} nodes in {ms:F1} ms");
            if (hold is not null)
            {
                Log.Write("HOLD", state);
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(hold + "-" + state) && deadline.Elapsed < TimeSpan.FromSeconds(90))
                {
                    await Task.Delay(50);
                }
            }
        }

        await DumpAsync("orders");
        await page.GetByRole("button", "Actions").ClickAsync();
        await Expect(page.GetByRole("menuitem", "Archive draft")).ToBeVisibleAsync();
        await DumpAsync("menu");
        await page.Keyboard.PressAsync("Escape");
        await page.GetByRole("button", "Place order").ClickAsync();
        await Expect(page.GetByRole("dialog", "Confirm order")).ToBeVisibleAsync();
        await DumpAsync("dialog");
        await view.CloseAsync();
        return 0;
    }

    /// <summary>
    /// User activation, measured by WebKit's popup blocker (a <c>create</c> request reaches the host only when
    /// window.open is allowed) and by navigator.userActivation, with waits longer than transient activation between
    /// routes: a timer with no input, an XTEST click, a script-dispatched click (run through the API), and window.open
    /// run directly through the API.
    /// </summary>
    public static async Task<int> ActivationAsync(XInput x)
    {
        var view = await View.CreateAsync();
        await view.NavigateAsync(Session.Origin + "/");
        var xtest = WindowPage.Attach(view, new XtestInput(x, "auto"));
        var script = WindowPage.Attach(view, new ScriptInput((v, request) => v.CallAsync("return globalThis.__sparAutomation.run(" + request + ");").ContinueWith(t => WindowPage.Parse(t.Result), TaskScheduler.Default)));
        await xtest.WaitForUiReadyAsync();
        await xtest.EvaluateAsync("""
            (window.__log = [], document.querySelector('h1').addEventListener('click', e => {
              const w = window.open('about:blank');
              window.__log.push('click trusted=' + e.isTrusted + ' isActive=' + navigator.userActivation.isActive + ' open=' + (w ? 'window' : 'null'));
            }), setTimeout(() => {
              const w = window.open('about:blank');
              window.__log.push('timer isActive=' + navigator.userActivation.isActive + ' hasBeenActive=' + navigator.userActivation.hasBeenActive + ' open=' + (w ? 'window' : 'null'));
            }, 6000), 0)
            """);
        // The point is computed first: any API call could itself count as a gesture, so none runs right before the click.
        var box = await xtest.EngineAsync("{\"op\":\"box\",\"chain\":" + xtest.GetByRole("heading", "Orders", exact: true).Chain + "}");
        var (px, py) = (box.GetProperty("x").GetDouble() + 20, box.GetProperty("y").GetDouble() + box.GetProperty("height").GetDouble() / 2);
        const int Wait = 6500;
        await Task.Delay(Wait);
        var afterTimer = view.NewWindowRequests;
        await xtest.Input.ClickAsync(view, px, py);
        await Task.Delay(300);
        var afterXtest = view.NewWindowRequests;
        await Task.Delay(Wait);
        await script.Input.ClickAsync(view, px, py);
        await Task.Delay(300);
        var afterScript = view.NewWindowRequests;
        await Task.Delay(Wait);
        var direct = (await xtest.EvaluateAsync("(() => { const a = navigator.userActivation.isActive; const w = window.open('about:blank'); return 'api isActive=' + a + ' open=' + (w ? 'window' : 'null'); })()")).GetString();
        var afterDirect = view.NewWindowRequests;
        Log.Write("ACTIVATION", $"create requests: timer (no input)={afterTimer}, XTEST click={afterXtest - afterTimer}, script click via API={afterScript - afterXtest}, window.open via API={afterDirect - afterScript}");
        Log.Write("ACTIVATION", "page log: " + string.Join(" | ", (await xtest.EvaluateAsync("window.__log")).EnumerateArray().Select(e => e.GetString())) + " | " + direct);
        await view.CloseAsync();
        return 0;
    }

    /// <summary>Holds one page open (for the remote-inspector probes of part 1).</summary>
    public static async Task<int> ServeAsync(int seconds, string output)
    {
        var view = await View.CreateAsync();
        await view.NavigateAsync(Session.Origin + "/");
        Log.Write("SERVE", $"ready url={Session.Origin}/ for {seconds}s");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        await view.CloseAsync();
        return 0;
    }

    private static TaskCompletionSource? s_webDriverClosed;

    /// <summary>
    /// The harness as the browser WebKitWebDriver launches: automation allowed on the context, a view controlled by
    /// automation for the session's <c>create-web-view</c>, and the view's <c>close</c> ending the process.
    /// </summary>
    public static unsafe Task<int> WebDriverHostAsync(int seconds)
    {
        s_webDriverClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui.Post(() => GLib.g_signal_connect_data(Session.Context, "automation-started", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnAutomationStarted, 0, 0, 0));
        Log.Write("WEBDRIVER-HOST", $"waiting for an automation session (WEBKIT_INSPECTOR_SERVER={Environment.GetEnvironmentVariable("WEBKIT_INSPECTOR_SERVER")})");
        return WaitAsync(seconds);
    }

    private static async Task<int> WaitAsync(int seconds)
    {
        await Task.WhenAny(s_webDriverClosed!.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        Log.Write("WEBDRIVER-HOST", $"done, closed by the session={s_webDriverClosed.Task.IsCompleted}");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnAutomationStarted(nint context, nint session, nint data)
    {
        Log.Write("WEBDRIVER-HOST", "automation-started");
        var info = WebKit.webkit_application_info_new();
        WebKit.webkit_application_info_set_name(info, "Harness");
        WebKit.webkit_application_info_set_version(info, 1, 0, 0);
        WebKit.webkit_automation_session_set_application_info(session, info);
        WebKit.webkit_application_info_unref(info);
        GLib.g_signal_connect_data(session, "create-web-view", (nint)(delegate* unmanaged<nint, nint, nint>)&OnCreateWebView, 0, 0, 0);
    }

    [UnmanagedCallersOnly]
    private static unsafe nint OnCreateWebView(nint session, nint data)
    {
        var view = View.Create(WebKit.webkit_web_context_get_network_session_for_automation(Session.Context), 1008, 729, automation: true);
        GLib.g_signal_connect_data(view.WebView, "close", (nint)(delegate* unmanaged<nint, nint, void>)&OnClose, 0, 0, 0);
        Gtk.gtk_window_present(view.Window);
        Log.Write("WEBDRIVER-HOST", "create-web-view answered");
        return view.WebView;
    }

    [UnmanagedCallersOnly]
    private static void OnClose(nint webView, nint data) => s_webDriverClosed?.TrySetResult();
}
