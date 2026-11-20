using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Spar.Linux;
using Spar.Testing;

namespace Spar.Tests;

/// <summary>
/// The Linux host in a real window (plan M1c, build step 1), driven from outside: the bridge probe app
/// (<c>tests/apps/BridgeProbe</c>, a Development build with its page, <c>-p:SparDevServer=false</c>) runs under the
/// test's own X server, with private folders under <c>artifacts/linux-homes/&lt;run&gt;/</c>, and the test reads its
/// log. Until the Linux Window surface and CI image (M1c step 7), these run where a runner provides both: Linux, the
/// probe's executable in <c>SPAR_LINUX_HOST_APP</c> and an X server that is not the user's desktop in
/// <c>SPAR_LINUX_TEST_DISPLAY</c> (never WSLg's). Anywhere else they skip, saying why.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(LinuxWindowsCollection))]
public sealed class LinuxHostTests
{
    [Fact]
    public async Task The_page_calls_moves_to_a_new_document_and_closes_through_the_close_request()
    {
        await using var app = LinuxAppRun.Start("facts,echo,navigate");

        await app.WaitForAsync(@"probe note: second document (?<doc>[0-9a-f]{16}), echo second");
        var log = app.Log();
        Assert.Matches(@"INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at [0-9.]+ ms", log);
        Assert.Contains("probe note: main world: handler undefined, relay undefined", log, StringComparison.Ordinal);
        Assert.Matches(@"probe note: echo mean [0-9.]+ ms over 1000", log);
        var documents = Regex.Matches(log, @"Bridge: document ([0-9a-f]{16}) opened\.").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(2, documents.Count);
        Assert.NotEqual(documents[0], documents[1]);
        Assert.Contains($"probe note: first document {documents[0]}", log, StringComparison.Ordinal);
        Assert.Contains($"probe note: second document {documents[1]}", log, StringComparison.Ordinal);
        Assert.Contains($"Bridge: document {documents[0]} closed (navigation).", log, StringComparison.Ordinal);

        var webProcesses = app.WebKitProcesses();
        Assert.NotEmpty(webProcesses);
        var exit = await app.CloseAsync();

        Assert.Equal(0, exit);
        log = app.Log();
        Assert.Contains("close requested (User)", log, StringComparison.Ordinal);
        Assert.Matches(@"web processes exited after [0-9]+ ms", log);
        Assert.Contains("services disposed", log, StringComparison.Ordinal);
        Assert.All(webProcesses, process => Assert.False(LinuxAppRun.IsRunning(process.Id), $"{process.Name} {process.Id} outlived the app."));
    }

    [Fact]
    public async Task A_frame_of_another_origin_cannot_reach_the_bridge()
    {
        // Skip before the foreign site starts: on Windows its listener goes through http.sys, and a port taken in the
        // meantime failed the test in a NativeAOT run where it had nothing to check.
        LinuxAppRun.Display();
        LinuxAppRun.Probe();
        using var foreign = ForeignSite.Start();
        await using var app = LinuxAppRun.Start("frame", ("BRIDGE_PROBE_FRAME", foreign.FrameAddress));

        await app.WaitForAsync("probe note: frame done");
        await foreign.WaitForAsync("done", LinuxAppRun.Patience);

        // The frame ran, and nothing it tried reached the backend: no note, no message the bridge had to refuse.
        Assert.Equal("absent", foreign.Reports["handler"]);
        Assert.StartsWith("blocked", foreign.Reports["parent-event"], StringComparison.Ordinal);
        Assert.Equal("sent", foreign.Reports["top-post"]);
        Assert.Equal("undefined", foreign.Reports["relay"]);
        var log = app.Log();
        Assert.DoesNotContain("from-frame", log, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", log, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(log, @"Bridge: document [0-9a-f]{16} opened\."));

        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task A_lost_web_process_is_recovered_once_and_the_page_connects_again()
    {
        await using var app = LinuxAppRun.Start("");
        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16}$");

        app.EndWebProcess();

        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16} \(reloaded\)");
        var log = app.Log();
        Assert.Contains("closed (web process ended)", log, StringComparison.Ordinal);
        Assert.Contains("page reloaded after the web process ended", log, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(log, @"Bridge: document [0-9a-f]{16} reported ui-ready\.").Count);

        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task A_second_loss_before_the_page_is_ready_again_ends_the_app_with_exit_code_5()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "Needs the Development build's automated mode: a Production app shows its message and waits.");
        await using var app = LinuxAppRun.Start("slow-ready-after-reload", ("SPAR_AUTOMATED", "1"));
        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16}$");

        app.EndWebProcess();
        await app.WaitForAsync("probe note: reloaded, ready in 3 s");
        app.EndWebProcess();

        Assert.Equal(5, await app.WaitForExitAsync());
        Assert.Contains("Exiting with code 5: The page stopped working again right after it was reloaded.", app.Log(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The M1c budgets on the Notebook's Production NativeAOT executable (plan, "Provisional budgets"), where the runner
    /// names it in <c>SPAR_LINUX_NOTEBOOK</c>: launch to <c>ui-ready</c> (from the test's clock at the launch to the
    /// time of the host's ready line), warm, one run discarded then 11; the close to the exit; and the total memory of
    /// the app and its WebKit processes (PSS) in one run. Recorded in
    /// <c>artifacts/test-results/Spar.Tests/linux-host-&lt;label&gt;.txt</c> (<c>SPAR_LINUX_NOTEBOOK_LABEL</c>, for runs
    /// with different settings in the environment), not asserted: times vary with the machine.
    /// </summary>
    [Fact]
    public async Task The_Notebook_starts_and_closes_and_its_numbers_are_recorded()
    {
        var notebook = Environment.GetEnvironmentVariable("SPAR_LINUX_NOTEBOOK");
        Assert.SkipWhen(string.IsNullOrEmpty(notebook), "Needs SPAR_LINUX_NOTEBOOK, the Notebook's published executable.");
        var ready = new List<double>();
        var closing = new List<double>();
        var memory = 0.0;
        for (var run = 0; run < 12; run++)
        {
            await using var app = LinuxAppRun.Launch(notebook!, "com.example.Notebook", []);
            var line = await app.WaitForAsync(@"^(?<time>\S+) INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at [0-9.]+ ms$");
            var readyAt = DateTime.Parse(line.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            if (run == 1)
            {
                // Settled first: the page has loaded its notes by now.
                await Task.Delay(1_000, TestContext.Current.CancellationToken);
                memory = app.TotalPssMegabytes();
            }

            var closed = Stopwatch.StartNew();
            Assert.Equal(0, await app.CloseAsync());
            if (run > 0)
            {
                ready.Add((readyAt - app.Launched).TotalMilliseconds);
                closing.Add(closed.Elapsed.TotalMilliseconds);
            }
        }

        // And once more with its web process lost: the page comes back by itself and the app still closes cleanly.
        await using (var lost = LinuxAppRun.Launch(notebook!, "com.example.Notebook", []))
        {
            await lost.WaitForAsync(@"Bridge: document [0-9a-f]{16} reported ui-ready\.");

            // The Notebook does not allow WebAssembly: its web process runs without it, and so does the one after a loss.
            Assert.Contains("WebAssembly off: JSC_useWasm=false.", lost.Log(), StringComparison.Ordinal);
            Assert.Equal("false", WebAssemblySetting(lost));
            lost.EndWebProcess();
            await lost.WaitForAsync(@"page reloaded after the web process ended(.|\n)*Bridge: document [0-9a-f]{16} reported ui-ready\.");
            Assert.Equal("false", WebAssemblySetting(lost));
            Assert.Equal(0, await lost.CloseAsync());
        }

        var size = new FileInfo(notebook!).Length;
        var results = Path.Combine(TestFolders.Artifacts(), "test-results", "Spar.Tests");
        Directory.CreateDirectory(results);
        await File.WriteAllLinesAsync(
            Path.Combine(results, $"linux-host-{Environment.GetEnvironmentVariable("SPAR_LINUX_NOTEBOOK_LABEL") ?? "default"}.txt"),
            [
                string.Create(CultureInfo.InvariantCulture, $"Recorded {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"),
                string.Create(CultureInfo.InvariantCulture, $"Notebook executable: {size} B"),
                $"Launch to ui-ready, warm: {Summary(ready)}",
                $"Close request to exit, from outside: {Summary(closing)}",
                string.Create(CultureInfo.InvariantCulture, $"Total PSS, app and WebKit processes, one run: {memory:0.0} MB"),
                "WebAssembly: JSC_useWasm=false in the web process, and in the one after a loss",
            ],
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The browser environment overrides of plan section 3c (M1c step 6), with every one set in the app's environment:
    /// without <c>AllowBrowserEnvironmentOverrides()</c> the inspector servers do not listen, the web process stays in
    /// its bubblewrap sandbox, and neither it nor the network process gets an override; a Production build also keeps
    /// JavaScriptCore's options from them, a Development build passes them. With the opt-in each one is in force. In
    /// every case <c>JSC_useWasm</c> is the declaration's, and the GPU workarounds reach the web process.
    /// </summary>
    /// <remarks>
    /// The two inspector servers are tried one at a time, the other left unset: with both set, WebKitGTK 2.52 opened only
    /// the HTTP one.
    /// </remarks>
    [Theory]
    [InlineData(false, "WEBKIT_INSPECTOR_SERVER")]
    [InlineData(false, "WEBKIT_INSPECTOR_HTTP_SERVER")]
    [InlineData(true, "WEBKIT_INSPECTOR_SERVER")]
    [InlineData(true, "WEBKIT_INSPECTOR_HTTP_SERVER")]
    public async Task The_browser_environment_overrides_are_ignored_unless_allowed_and_the_GPU_workarounds_pass(bool allowed, string server)
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        var port = FreePort();
        var other = server == "WEBKIT_INSPECTOR_SERVER" ? "WEBKIT_INSPECTOR_HTTP_SERVER" : "WEBKIT_INSPECTOR_SERVER";
        (string Name, string? Value)[] variables =
        [
            ("BRIDGE_PROBE_PLAN", "jsc"),
            ("BRIDGE_PROBE_BROWSER", allowed ? "AllowBrowserEnvironmentOverrides" : null),
            (server, $"127.0.0.1:{port}"),
            (other, null),
            ("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "1"),
            ("JSC_useDollarVM", "true"),
            ("JSC_useWasm", "true"),
            ("__NV_DISABLE_EXPLICIT_SYNC", "1"),
            ("WEBKIT_DISABLE_COMPOSITING_MODE", "1"),
        ];
        await using var app = LinuxAppRun.Launch(LinuxAppRun.Probe(), LinuxAppRun.ProbeAppId, variables);

        var jsc = await app.WaitForAsync(@"probe note: jsc: (?<seen>.+)$");
        var log = app.Log();
        var webProcess = app.WebKitProcesses().First(process => process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal));
        var network = app.WebKitProcesses().First(process => process.Name.StartsWith("WebKitNetworkPr", StringComparison.Ordinal));
        var environment = LinuxAppRun.EnvironmentOf(webProcess.Id);
        var networkEnvironment = LinuxAppRun.EnvironmentOf(network.Id);
        var listening = ListeningPorts();
        var sandboxed = Ancestors(webProcess.Id, app.Id).Contains("bwrap");

        foreach (var name in EngineEnvironment.Overrides().Where(name => name != other))
        {
            Assert.Contains(allowed ? $"{name} in force (AllowBrowserEnvironmentOverrides)." : $"{name} ignored (AllowBrowserEnvironmentOverrides allows it).", log, StringComparison.Ordinal);
            Assert.Equal(allowed, environment.ContainsKey(name));
            Assert.Equal(allowed, networkEnvironment.ContainsKey(name));
        }

        var jscKept = allowed || development;
        Assert.Contains(
            (allowed, development) switch
            {
                (true, _) => "JSC_useDollarVM in force (AllowBrowserEnvironmentOverrides)",
                (false, true) => "JSC_useDollarVM in force (a Development build keeps JavaScriptCore options)",
                _ => "JSC_useDollarVM ignored: a JavaScriptCore option (AllowBrowserEnvironmentOverrides allows it).",
            },
            log,
            StringComparison.Ordinal);
        Assert.Equal(jscKept, environment.ContainsKey("JSC_useDollarVM"));
        Assert.Equal("false", environment["JSC_useWasm"]);
        Assert.Equal(allowed, log.Contains("Browser opt-ins in force: AllowBrowserEnvironmentOverrides()", StringComparison.Ordinal));

        // The inspector server listens only with the opt-in, and the sandbox goes only with it.
        Assert.Equal(allowed, listening.Contains(port));
        Assert.Equal(!allowed, sandboxed);

        // The GPU workarounds pass in every case, and Spar's own software-GPU default reaches the web process too.
        Assert.Equal("1", environment["__NV_DISABLE_EXPLICIT_SYNC"]);
        Assert.Equal("1", environment["WEBKIT_DISABLE_COMPOSITING_MODE"]);
        Assert.True(environment.ContainsKey("WEBKIT_DISABLE_DMABUF_RENDERER"), "Spar's software-GPU default did not reach the web process.");

        // Even where the option reaches JavaScriptCore, a release WebKitGTK refuses it ("invalid option": useDollarVM is
        // one of its restricted options), so no debugging object reaches the page either way.
        Assert.StartsWith("$vm undefined", jsc.Groups["seen"].Value, StringComparison.Ordinal);
        Assert.Equal(jscKept, app.Output.Contains("invalid option: JSC_useDollarVM=true", StringComparison.Ordinal));

        Assert.Equal(0, await app.CloseAsync());
        Record(
            $"{(development ? "development" : "production")}, {(allowed ? "AllowBrowserEnvironmentOverrides()" : "default")}: " +
            $"{server} {(listening.Contains(port) ? "listening" : "closed")}, " +
            $"web process {(sandboxed ? "under bwrap" : "without bwrap")}, JSC_useDollarVM {(environment.ContainsKey("JSC_useDollarVM") ? "kept" : "removed")}, " +
            $"page {jsc.Groups["seen"].Value}");
    }

    /// <summary>
    /// The inspectors on the published executable: WebKit's (developer extras) off in Production, on in a Development
    /// build and with <c>AllowDevTools()</c>; GTK's own, which GTK 4 opens on Ctrl+Shift+I in every app by default,
    /// kept closed outside a Development build. With real input on the run's own X server: Ctrl+Shift+I, which a
    /// release WebKitGTK does not bind, after a right click on plain text, whose menu holds only Inspect Element where
    /// devtools are on and shows nothing where they are off.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_inspector_follows_the_build_and_AllowDevTools(bool allowed)
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        var on = allowed || development;
        await using var app = LinuxAppRun.Start("menu:plain", ("BRIDGE_PROBE_BROWSER", allowed ? "AllowDevTools" : null));
        await app.WaitForAsync(@"probe note: menu ready \(plain\)");

        Assert.Matches($@"Browser settings (set|read back) \([0-9.]+ ms\): devtools {(on ? "on" : "off")}, GTK inspector keys {(development ? "on" : "off")}, script clipboard off, script windows off, file access off, universal file access off, data: navigation off; menu cut down, downloads denied, picker cancelled, history cancelled\.", app.Log());
        var display = LinuxAppRun.Display();
        var beforeMenu = app.WindowCount();
        XTest.ClickMiddle(display, X11Frames.MainWindow(display, app.Id), button: 3);
        await Task.Delay(800, TestContext.Current.CancellationToken);
        var menu = X11Frames.Titles(display, app.Id);
        XTest.Press(display, "Escape");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // Last, since GTK's inspector opens over the window where it opens at all.
        XTest.ClickMiddle(display, X11Frames.MainWindow(display, app.Id));
        var before = X11Frames.Titles(display, app.Id);
        XTest.Press(display, "Control_L+Shift_L+i");
        await Task.Delay(1_500, TestContext.Current.CancellationToken);
        var afterKey = X11Frames.Titles(display, app.Id);
        var pagesAfterKey = WebPages(app);
        Record(
            $"{(development ? "development" : "production")}, {(allowed ? "AllowDevTools()" : "default")}: windows [{string.Join(" | ", before)}]; " +
            $"after Ctrl+Shift+I [{string.Join(" | ", afterKey)}], {pagesAfterKey} web processes; {beforeMenu} windows before the right click, " +
            $"[{string.Join(" | ", menu)}] with the menu asked");

        // GTK's own inspector opens on its keys only in a Development build; where devtools are off, plain text has no
        // menu at all, and where they are on, its menu is Inspect Element alone.
        Assert.Equal(development, afterKey.Count > before.Count);
        Assert.Equal(1, pagesAfterKey);
        Assert.Equal(on, menu.Count > beforeMenu);
        if (development)
        {
            Assert.Contains("Context menu (not editable): kept [inspectElement], removed ", app.Log(), StringComparison.Ordinal);
        }

        // The main window only: GTK's inspector, where it opened, is not the app's to close.
        app.RequestClose();
        Assert.Equal(0, await app.WaitForExitAsync());
    }

    /// <summary>
    /// <c>GTK_DEBUG=interactive</c> in the app's environment, which GTK reads itself at its start and which opens GTK's
    /// inspector (user decision 2026-11-20): a Production build removes it before GTK starts unless the app calls
    /// <c>AllowBrowserEnvironmentOverrides()</c>, so only the app's window opens; a Development build keeps it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GTK_DEBUG_interactive_opens_GTKs_inspector_only_in_Development_or_with_browser_environment_overrides(bool allowed)
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        await using var app = LinuxAppRun.Start(
            "",
            ("GTK_DEBUG", "interactive"),
            ("BRIDGE_PROBE_BROWSER", allowed ? "AllowBrowserEnvironmentOverrides" : null));
        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16}$");
        await Task.Delay(1_500, TestContext.Current.CancellationToken);
        var titles = X11Frames.Titles(LinuxAppRun.Display(), app.Id);

        Record(
            $"{(development ? "development" : "production")}, GTK_DEBUG=interactive, " +
            $"{(allowed ? "AllowBrowserEnvironmentOverrides()" : "default")}: windows [{string.Join(" | ", titles)}]");
        var inForce = allowed || development;
        Assert.Equal(inForce ? 2 : 1, titles.Count);
        Assert.Contains(inForce ? "GTK_DEBUG in force" : "GTK_DEBUG ignored", app.Log(), StringComparison.Ordinal);
        app.RequestClose();
        Assert.Equal(0, await app.WaitForExitAsync());
    }
    private static int WebPages(LinuxAppRun app) =>
        app.WebKitProcesses().Count(process => process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal));
    /// <summary>
    /// The browser's own keys on the published executable, pressed with real input: the page gets each one, and none
    /// reloads, zooms, prints, finds or goes back (WebKitGTK binds no such key itself).
    /// </summary>
    [Fact]
    public async Task The_browser_keys_reach_the_page_and_do_nothing_else()
    {
        await using var app = LinuxAppRun.Start("keys");
        await app.WaitForAsync("probe note: keys ready");
        var display = LinuxAppRun.Display();
        XTest.ClickMiddle(display, X11Frames.MainWindow(display, app.Id));
        string[] keys =
        [
            "F5", "Control_L+r", "Control_L+Shift_L+r", "Alt_L+Left", "Alt_L+Right", "Control_L+p", "Control_L+f",
            "Control_L+equal", "Control_L+minus", "Control_L+0", "F12", "Control_L+s", "Control_L+o",
        ];
        foreach (var key in keys)
        {
            XTest.Press(display, key);
            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        XTest.Press(display, "Escape");
        var seen = await app.WaitForAsync(@"probe note: keys: (?<seen>.+)$");
        var windows = app.WindowCount();
        var log = app.Log();

        Assert.Matches(@"zoom (?<zoom>\S+) -> \k<zoom>, at /", seen.Groups["seen"].Value);
        Assert.DoesNotContain("(reloaded)", log, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(log, @"Bridge: document [0-9a-f]{16} opened\."));
        Assert.Equal(1, windows);
        Assert.Equal(0, await app.CloseAsync());
        Record($"{(LinuxAppRun.IsDevelopmentProbe() ? "development" : "production")}: {string.Join(" ", keys)}: {seen.Groups["seen"].Value}; {windows} window");
    }

    /// <summary>
    /// Downloads, the page's file picker and the context menu (M1c step 6), with real input for the picker and the
    /// menu: refused by default, and the browser's own with <c>AllowDownloads()</c>, <c>AllowPagePicker()</c> and
    /// <c>AllowContextMenu()</c>. Only a Development build describes its menus in the log.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Downloads_the_picker_and_the_context_menu_are_refused_unless_allowed(bool allowed)
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        var run = Path.Combine(TestFolders.Artifacts(), "linux-homes", "host-tests", Guid.NewGuid().ToString("N")[..12]);
        var downloads = Path.Combine(run, "home", "Downloads");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(Path.Combine(run, "config"));
        File.WriteAllText(Path.Combine(run, "config", "user-dirs.dirs"), "XDG_DOWNLOAD_DIR=\"$HOME/Downloads\"\n");
        await using var app = LinuxAppRun.Launch(
            LinuxAppRun.Probe(),
            LinuxAppRun.ProbeAppId,
            [("BRIDGE_PROBE_PLAN", "download,picker,menu:editable"), ("BRIDGE_PROBE_BROWSER", allowed ? "AllowDownloads,AllowPagePicker,AllowContextMenu" : null)],
            runFolder: run);
        var display = LinuxAppRun.Display();

        await app.WaitForAsync("probe note: download: ");
        await Task.Delay(2_000, TestContext.Current.CancellationToken);
        var cancelled = Regex.Matches(app.Log(), @"A download( of \S+)? was cancelled \(AllowDownloads allows it\)\.").Count;
        var refusedByLock = Regex.Matches(app.Log(), @"Navigation lock: a navigation to (blob|data):").Count;
        var written = Directory.EnumerateFiles(app.RunFolder, "*", SearchOption.AllDirectories)
            .Count(path => Path.GetFileName(path) is "other.html" or "probe.txt" or "data.txt");

        await app.WaitForAsync("probe note: picker ready");
        XTest.ClickMiddle(display, X11Frames.MainWindow(display, app.Id));
        await Task.Delay(1_500, TestContext.Current.CancellationToken);
        var pickerWindows = app.WindowCount();
        if (allowed)
        {
            XTest.Press(display, "Escape");
        }

        var picker = await app.WaitForAsync(@"probe note: picker: (?<answer>.+)$", TimeSpan.FromSeconds(12));

        await app.WaitForAsync(@"probe note: menu ready \(editable\)");
        XTest.ClickMiddle(display, X11Frames.MainWindow(display, app.Id), button: 3);
        await Task.Delay(1_000, TestContext.Current.CancellationToken);
        var menuWindows = app.WindowCount();
        XTest.Press(display, "Escape");
        var log = app.Log();

        Assert.Equal(allowed ? 0 : 1, cancelled);
        Assert.Equal(allowed, !log.Contains("The page's file picker was cancelled (AllowPagePicker allows it).", StringComparison.Ordinal));
        Assert.Equal(0, allowed ? 0 : written);
        if (development)
        {
            Assert.Equal(
                !allowed,
                log.Contains("Context menu (editable): kept [cut copy paste selectAll inspectElement], removed ", StringComparison.Ordinal));
        }

        Assert.Equal(0, await app.CloseAsync());
        Record(
            $"{(development ? "development" : "production")}, {(allowed ? "AllowDownloads() AllowPagePicker() AllowContextMenu()" : "default")}: " +
            $"downloads cancelled {cancelled}, refused by the lock {refusedByLock}, files written {written}; picker '{picker.Groups["answer"].Value}', " +
            $"{pickerWindows} windows while it was asked; {menuWindows} windows after the right click");
    }

    /// <summary>
    /// History navigation (M1c step 6): a move within the document goes back; back to another document, from the
    /// page's <c>history.back()</c> and from the mouse's back button, is cancelled unless <c>AllowHistoryNavigation()</c>.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_navigation_to_another_document_is_cancelled_unless_allowed(bool allowed)
    {
        await using var app = LinuxAppRun.Start("history", ("BRIDGE_PROBE_BROWSER", allowed ? "AllowHistoryNavigation" : null));

        await app.WaitForAsync("probe note: history: within the document back to none");
        await app.WaitForAsync("probe note: history: going back from the second document");
        if (!allowed)
        {
            await app.WaitForAsync("probe note: history: still at /other.html");
            var display = LinuxAppRun.Display();
            XTest.ClickMiddle(display, X11Frames.MainWindow(display, app.Id), button: 8);
            await Task.Delay(1_500, TestContext.Current.CancellationToken);
        }
        else
        {
            await Task.Delay(3_000, TestContext.Current.CancellationToken);
        }

        var log = app.Log();
        var cancelled = Regex.Matches(log, @"Back or forward navigation cancelled \(AllowHistoryNavigation allows it\)\.").Count;
        var commits = Regex.Matches(log, @"Bridge: document [0-9a-f]{16} opened\.").Count;

        Assert.Equal(allowed, cancelled == 0);
        Assert.Equal(allowed, !log.Contains("probe note: history: still at", StringComparison.Ordinal));
        Assert.Equal(allowed ? 3 : 2, commits);
        Assert.Equal(0, await app.CloseAsync());
        Record($"{(LinuxAppRun.IsDevelopmentProbe() ? "development" : "production")}, {(allowed ? "AllowHistoryNavigation()" : "default")}: {cancelled} cancelled, {commits} documents committed");
    }

    /// <summary>The loopback ports something listens on (TCP, IPv4 and IPv6), from <c>/proc/net/tcp</c> and <c>tcp6</c>.</summary>
    private static HashSet<int> ListeningPorts()
    {
        var ports = new HashSet<int>();
        foreach (var table in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            foreach (var line in File.ReadLines(table).Skip(1))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > 3 && fields[3] == "0A")
                {
                    ports.Add(int.Parse(fields[1][(fields[1].LastIndexOf(':') + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }
            }
        }

        return ports;
    }


    /// <summary>The names of the processes between <paramref name="process"/> and <paramref name="root"/>, nearest first.</summary>
    private static List<string> Ancestors(int process, int root)
    {
        var names = new List<string>();
        var current = process;
        while (current != root && current > 1)
        {
            var stat = File.ReadAllText($"/proc/{current}/stat");
            current = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1], CultureInfo.InvariantCulture);
            if (current != root)
            {
                names.Add(File.ReadAllText($"/proc/{current}/comm").Trim());
            }
        }

        return names;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void Record(string line)
    {
        var results = Path.Combine(TestFolders.Artifacts(), "test-results", "Spar.Tests");
        Directory.CreateDirectory(results);
        File.AppendAllLines(Path.Combine(results, "linux-security-defaults.txt"), [string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss} {line}")]);
    }

    /// <summary><c>JSC_useWasm</c> as the app's web process started with it, waiting for the process if it is not there yet.</summary>
    private static string? WebAssemblySetting(LinuxAppRun app)
    {
        var limit = Stopwatch.StartNew();
        while (limit.Elapsed < LinuxAppRun.Patience)
        {
            foreach (var (id, name) in app.WebKitProcesses())
            {
                try
                {
                    if (name.StartsWith("WebKitWebProces", StringComparison.Ordinal)
                        && LinuxAppRun.EnvironmentOf(id).TryGetValue("JSC_useWasm", out var value))
                    {
                        return value;
                    }
                }
                catch (IOException)
                {
                    // The process ended meanwhile.
                }
            }

            Thread.Sleep(20);
        }

        return null;
    }

    internal static string Summary(List<double> milliseconds)
    {
        var sorted = milliseconds.Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        var values = string.Join(", ", milliseconds.Select(value => value.ToString("0.0", CultureInfo.InvariantCulture)));
        return string.Create(CultureInfo.InvariantCulture, $"median {median:0.0} ms ({sorted[0]:0.0}–{sorted[^1]:0.0}, n = {sorted.Length}): {values}");
    }

    /// <summary>
    /// A site of another origin on loopback, whose page the probe embeds in a frame: its script tries every way to the
    /// bridge it might have and reports each outcome to its own server, never through the app.
    /// </summary>
    private sealed class ForeignSite : IDisposable
    {
        private const string Page = """
            <!doctype html><meta charset="utf-8"><title>Foreign</title>
            <script>
            const report = (key, value) => fetch("/report?" + key + "=" + encodeURIComponent(value));
            const call = text => '{"type":"call","id":900,"contract":"probe","command":"note"}\t' + JSON.stringify(text);
            (async () => {
              const handler = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.spar;
              await report("handler", handler ? "present" : "absent");
              if (handler) {
                try { handler.postMessage(call("from-frame-handler")); } catch (e) { }
              }
              try {
                parent.dispatchEvent(new CustomEvent("spar:send", { detail: call("from-frame-parent") }));
                await report("parent-event", "dispatched");
              } catch (e) {
                await report("parent-event", "blocked " + e.name);
              }
              try {
                top.postMessage(call("from-frame-post-message"), "*");
                await report("top-post", "sent");
              } catch (e) {
                await report("top-post", "threw " + e.name);
              }
              window.dispatchEvent(new CustomEvent("spar:send", { detail: call("from-frame-own-window") }));
              await report("relay", typeof sparRelay);
              await report("done", "1");
            })();
            </script>
            """;

        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<string, string> _reports = new(StringComparer.Ordinal);
        private readonly Task _serving;

        private ForeignSite(int port)
        {
            FrameAddress = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/frame.html";
            _listener.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public string FrameAddress { get; }

        public IReadOnlyDictionary<string, string> Reports => _reports;

        public static ForeignSite Start()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return new ForeignSite(port);
        }

        public async Task WaitForAsync(string key, TimeSpan patience)
        {
            var limit = Stopwatch.StartNew();
            while (!_reports.ContainsKey(key))
            {
                Assert.True(limit.Elapsed < patience, $"The foreign frame never reported '{key}': {string.Join(", ", _reports)}");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            _serving.Wait(TimeSpan.FromSeconds(2));
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? "";
                if (path == "/report")
                {
                    foreach (var key in context.Request.QueryString.AllKeys.OfType<string>())
                    {
                        _reports[key] = context.Request.QueryString[key] ?? "";
                    }
                }

                var body = Encoding.UTF8.GetBytes(path == "/frame.html" ? Page : "");
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }
    }
}
