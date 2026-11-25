using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.WindowTests;

/// <summary>
/// Page security on the Linux host (plan M1c, build step 4), driven from outside like <see cref="LinuxHostTests"/>: the
/// bridge probe's page tries to leave, reads its headers, raises violations and asks for permissions as the test's plan
/// says, and the test reads the host's log. The one click is real input (XTEST) on the tests' private Xvfb, on a link
/// whose address goes to a stub handler the test writes into the run's own XDG folders, after checking that GIO picks
/// it; nothing reaches a real browser or the network beyond a loopback site of the test's own. Numbers go to
/// <c>artifacts/test-results/Spar.WindowTests/linux-page-security.txt</c>.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(ProbeCollection))]
[Trait("Surface", "BlackBox")]
[Trait("App", "BridgeProbe")]
public sealed class LinuxPageSecurityTests
{
    private const string Note = @"^\S+ INFO  BridgeProbe\.ProbeService  probe note: ";

    private const string Locked = @"^\S+ WARN  Spar\.Host  \[[0-9a-f]{8}\] Navigation lock: ";

    [Fact]
    public async Task Navigations_by_script_leave_the_page_on_its_document_and_each_is_logged()
    {
        await using var app = await ProbeApp.StartAsync("nav-script");

        var stayed = await app.WaitForAsync(Note + "nav: still on (?<at>\\S+), same document (?<same>\\w+)$");

        Assert.Equal("spar://app/", stayed.Groups["at"].Value);
        Assert.Equal("true", stayed.Groups["same"].Value);
        var log = app.Log();
        foreach (var address in new[]
        {
            "https://example.invalid/top", "spar://other/", "data:text/html,<p>data</p>", "https://example.invalid/link",
            "https://example.invalid/refresh",
        })
        {
            Assert.Contains($"Navigation lock: a navigation to {address} was ignored", log, StringComparison.Ordinal);
        }

        Assert.Matches("Navigation lock: a navigation to blob:spar://app/[0-9a-f-]+ was ignored", log);

        // The engine refused these before the lock was asked: a file: address from spar://, a form by form-action
        // 'none', and new windows without a user's click by its popup blocker (window.open answers null).
        Assert.DoesNotContain("file:///etc/hostname was ignored", log, StringComparison.Ordinal);
        Assert.Contains("Content Security Policy: form-action blocked https://example.invalid/form", log, StringComparison.Ordinal);
        Assert.Contains("window.open: null", log, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(log, @"Bridge: document [0-9a-f]{16} opened\."));
        Record($"{Build()}: navigations by script: {Regex.Matches(log, Locked, RegexOptions.Multiline).Count} ignored by the lock, the page stayed on its document");

        Assert.Equal(0, await app.CloseAsync());
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("srcdoc")]
    public async Task A_frames_first_document_is_refused_in_the_window_and_the_page_stays(string which)
    {
        await using var app = await ProbeApp.StartAsync($"nav-about:{which}");

        var stayed = await app.WaitForAsync(Note + "nav-about: still on (?<at>\\S+), same document (?<same>\\w+)$");

        Assert.Equal("spar://app/", stayed.Groups["at"].Value);
        Assert.Equal("true", stayed.Groups["same"].Value);
        Assert.Contains($"Navigation lock: a navigation to about:{which} was ignored", app.Log(), StringComparison.Ordinal);
        Assert.DoesNotContain("committed; it has no bridge", app.Log(), StringComparison.Ordinal);
        Assert.Equal(0, await app.CloseAsync());
        Assert.DoesNotContain("Web processes still running", app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_frame_address_loads_only_in_a_frame_and_a_redirect_away_from_it_is_blocked()
    {
        ProbeApp.Display();
        ProbeApp.Probe();
        using var site = LoopbackSite.Start();
        await using var app = await ProbeApp.StartAsync("frames,nav-declared", ("BRIDGE_PROBE_FRAME", site.Page));

        var frames = await app.WaitForAsync(Note + "frames: blank (?<blank>\\S+), srcdoc (?<srcdoc>\\S+)$");
        Assert.Equal("about:blank", frames.Groups["blank"].Value);
        // The lock cannot tell a frame from the window, so it refuses both documents: a frame keeps its first, blank one.
        Assert.Equal("none", frames.Groups["srcdoc"].Value);
        // The page's frame-src refuses the redirect's target before the lock is asked; it names the origin only.
        await app.WaitForAsync(@"WARN  Spar.Host  Content Security Policy: frame-src blocked http://example.invalid at ");
        Assert.DoesNotContain("after-redirect", app.Log(), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(app.Log(), "frame-src blocked http://example.invalid at "));
        var stayed = await app.WaitForAsync(Note + "nav-declared: still on (?<at>\\S+), same document (?<same>\\w+)$");

        Assert.Equal("spar://app/", stayed.Groups["at"].Value);
        Assert.Equal("true", stayed.Groups["same"].Value);
        Assert.Contains($"Navigation lock: the window started to load {site.Page}, an address declared for frames only; the load was stopped.", app.Log(), StringComparison.Ordinal);
        Assert.DoesNotContain("arrived between documents", app.Log(), StringComparison.Ordinal);

        // The declared site saw the redirecting frame's request; the window's was stopped as it started, before or just
        // after it was sent (recorded), and never got a response the window used.
        Assert.Contains("/frame.html?redirect=1", site.Requests);
        Assert.Equal(0, await app.CloseAsync());
        var stray = app.Log().Contains("with no window left", StringComparison.Ordinal);
        Assert.DoesNotContain("WebKit processes still running", app.Log(), StringComparison.Ordinal);
        Record($"{Build()}: a declared frame address in the window was stopped as it started; the site saw [{string.Join(", ", site.Requests)}]; a web process left without a window ended at exit: {stray}");
    }

    [Fact]
    public async Task Every_response_carries_the_policy_and_the_headers_the_worker_and_404_included()
    {
        await using var app = await ProbeApp.StartAsync("headers");

        var line = await app.WaitForAsync(Note + "headers: (?<json>.+)$");

        using var seen = JsonDocument.Parse(line.Groups["json"].Value);
        foreach (var (name, status, type) in new[]
        {
            ("document", 200, "text/html; charset=utf-8"),
            ("module", 200, "text/javascript; charset=utf-8"),
            ("worker", 200, "text/javascript; charset=utf-8"),
            ("missing", 404, null),
        })
        {
            var headers = seen.RootElement.GetProperty(name);
            Assert.Equal(status, headers.GetProperty("status").GetInt32());
            if (type is not null)
            {
                Assert.Equal(type, headers.GetProperty("content-type").GetString());
            }

            var policy = headers.GetProperty("content-security-policy").GetString() ?? "";
            Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
            Assert.Contains("connect-src spar://app;", policy, StringComparison.Ordinal);
            Assert.Contains("require-trusted-types-for 'script'", policy, StringComparison.Ordinal);
            Assert.Contains("report-uri /_spar/csp-report", policy, StringComparison.Ordinal);
            Assert.Equal("nosniff", headers.GetProperty("x-content-type-options").GetString());
            Assert.Equal("no-store", headers.GetProperty("cache-control").GetString());
            Assert.Equal("no-referrer", headers.GetProperty("referrer-policy").GetString());
            Assert.Equal("same-origin", headers.GetProperty("cross-origin-opener-policy").GetString());
            Assert.Equal("same-origin", headers.GetProperty("cross-origin-resource-policy").GetString());
            Assert.Contains("geolocation=(self)", headers.GetProperty("permissions-policy").GetString(), StringComparison.Ordinal);
            Assert.Contains("camera=()", headers.GetProperty("permissions-policy").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task A_worker_gets_the_policy_from_its_own_response()
    {
        await using var app = await ProbeApp.StartAsync("worker");

        var line = await app.WaitForAsync(Note + "worker: (?<json>.+)$");

        using var result = JsonDocument.Parse(line.Groups["json"].Value);
        Assert.StartsWith("blocked", result.RootElement.GetProperty("eval").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("blocked", result.RootElement.GetProperty("function").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("refused", result.RootElement.GetProperty("fetch").GetString(), StringComparison.Ordinal);

        // WebKitGTK raises no event and sends no report for a violation in a worker (docs/evidence/linux-gpu-csp): recorded,
        // so a WebKit that starts reporting them shows here.
        var reported = app.Log().Contains("blocked https://blocked.invalid/from-worker", StringComparison.Ordinal);
        Record($"{Build()}: worker {line.Groups["json"].Value}; its violation reported: {reported}");
        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task One_violation_reaches_the_host_by_both_channels_and_is_logged_once()
    {
        await using var app = await ProbeApp.StartAsync("violation");
        await app.WaitForAsync(Note + "violation: raised 1$");

        Assert.Equal(0, await app.CloseAsync());

        var log = app.Log();
        Assert.Single(Regex.Matches(log, @"Content Security Policy: img-src blocked https://blocked\.invalid/one\.png at "));
        var totals = Totals(log);
        Assert.Equal(1, totals.Batches);
        Assert.Equal(1, totals.Read);
        Assert.Equal(0, totals.Refused + totals.Dropped);
        Assert.DoesNotMatch(@"violations in the last \d+ s: .*one\.png", log);
        Record($"{Build()}: one violation: 1 line, {totals.Batches} listener batch, {totals.Read} report-uri report read");
    }

    [Fact]
    public async Task A_flood_of_distinct_violations_stays_within_the_limits()
    {
        var (raised, totals, log) = await FloodAsync(2_000, []);

        // The listener reports at most 100 groups per document and counts the rest; it stops after 1,000 and says so.
        // report-uri is read within its rate (20 at once, then 5 a second), every other report answered unread.
        Assert.InRange(Regex.Matches(log, @"Content Security Policy: img-src blocked https://blocked\.invalid/\d+\.png at ").Count, 1, 100);
        Assert.Matches(@"violations in the last [0-9.]+ s: .*past the group limit: \d+", log);
        Assert.Single(Regex.Matches(log, "Spar's listener stopped counting them"));
        Assert.InRange(totals.Batches, 1, 10);
        Assert.InRange(totals.Read, 1, 20 + (5 * 10));
        Record($"{Build()}: 2,000 distinct violations raised in {raised:0.0} ms: {Regex.Matches(log, "WARN  Spar.Host  Content Security Policy: ").Count} warning lines, {totals.Batches} listener batches, {totals.Read} report-uri read, {totals.Refused} refused unread, {totals.Dropped} dropped");
    }

    /// <summary>
    /// The flood numbers of <c>docs/evidence/m1c-page-security.md</c>, only where the runner asks for them
    /// (<c>SPAR_LINUX_FLOOD</c>, the counts separated by spaces): a long flood keeps a page busy, and a Production probe
    /// then shows the hung-page dialog on the private display. A measurement, not a check: explicit, so a run takes it
    /// only when asked (<c>--explicit only</c>, with <c>SPAR_LINUX_FLOOD</c>), and CI, which runs every black-box test with
    /// no skip, leaves it out by its trait <c>Kind=Measurement</c> (an explicit test still counts as skipped in a run's totals).
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Kind", "Measurement")]
    public async Task The_flood_numbers_are_recorded()
    {
        var counts = Environment.GetEnvironmentVariable("SPAR_LINUX_FLOOD");
        Assert.SkipWhen(string.IsNullOrEmpty(counts), "Needs SPAR_LINUX_FLOOD, the flood sizes to measure.");
        foreach (var count in counts!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(text => int.Parse(text, CultureInfo.InvariantCulture)))
        {
            var (raised, totals, log) = await FloodAsync(count, ProbeApp.IsDevelopmentProbe() ? [("SPAR_AUTOMATED", "1")] : []);
            var lines = Regex.Matches(log, @"WARN  Spar\.Host  Content Security Policy: ").Count;
            var summaries = Regex.Matches(log, "violations in the last").Count;
            var alarms = Regex.Matches(log, "The page is not responding").Count;
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{Build()}: {count} distinct raised in {raised:0} ms; {lines} warning lines, {summaries} summaries, {totals.Batches} listener batches, {totals.Read} report-uri read, {totals.Refused} refused, {totals.Dropped} dropped, {alarms} hang alarms");
            File.AppendAllLines(Path.Combine(ResultsFolder(), "linux-csp-flood.txt"), [line]);
            Record(line);
        }
    }

    [Fact]
    public async Task Only_a_declared_permission_is_granted_and_every_other_request_is_denied_without_a_prompt()
    {
        await using var app = await ProbeApp.StartAsync("permissions");

        var line = await app.WaitForAsync(Note + "permissions: (?<json>.+)$");

        using var seen = JsonDocument.Parse(line.Groups["json"].Value);
        var results = seen.RootElement;

        // Geolocation is declared: granted, so the position fails only for want of a location service (2 or 3), not 1.
        Assert.NotEqual("error 1", results.GetProperty("geolocation").GetString());
        Assert.Equal("denied", results.GetProperty("notifications").GetString());
        Assert.DoesNotContain("shown", results.GetProperty("devices").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("refused", results.GetProperty("clipboard").GetString(), StringComparison.Ordinal);
        // The host answers each query "denied" for what is undeclared; WebKit shows a "denied" to the page as "prompt",
        // and a feature it granted in this document as "granted". An undeclared one is never "granted".
        Assert.Equal("granted", results.GetProperty("query geolocation").GetString());
        foreach (var name in new[] { "notifications", "camera", "microphone" })
        {
            Assert.Equal("prompt", results.GetProperty($"query {name}").GetString());
        }

        var log = app.Log();
        Assert.Matches(@"Permission request for geolocation from spar://app/\S*: granted \(declared\)", log);
        // Notifications are refused by WebKit itself here (no request reaches the host); a request would be denied.
        Assert.Equal(1, app.WindowCount());
        var kinds = string.Join(", ", Regex.Matches(log, @"Permission request for (?<kind>.+?) from \S+: (?<answer>granted|denied)")
            .Select(match => $"{match.Groups["kind"].Value} {match.Groups["answer"].Value}"));
        Record($"{Build()}: permissions {line.Groups["json"].Value}; requests the host saw: {kinds}");

        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task Requests_a_click_makes_the_page_ask_for_are_denied_when_not_declared()
    {
        await using var app = await ProbeApp.StartAsync("permissions-click");
        await app.WaitForAsync(Note + "permissions-click: ready$");

        XTest.ClickMiddle(ProbeApp.Display(), X11Frames.MainWindow(ProbeApp.Display(), app.Id));

        var line = await app.WaitForAsync(Note + "permissions-click: (?<json>\\{.+)$");
        using var seen = JsonDocument.Parse(line.Groups["json"].Value);
        Assert.NotEqual("granted", seen.RootElement.GetProperty("notifications").GetString());
        Assert.NotEqual("read", seen.RootElement.GetProperty("clipboard").GetString());
        Assert.NotEqual("locked", seen.RootElement.GetProperty("pointerLock").GetString());
        var log = app.Log();
        Assert.DoesNotMatch("Permission request for .+: granted", log);
        Assert.Equal(1, app.WindowCount());
        var kinds = string.Join(", ", Regex.Matches(log, @"Permission request for (?<kind>.+?) from \S+: (?<answer>granted|denied)")
            .Select(match => $"{match.Groups["kind"].Value} {match.Groups["answer"].Value}"));
        Record($"{Build()}: after a click {line.Groups["json"].Value}; requests the host saw: {kinds}");
        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task A_users_click_on_an_external_link_opens_it_with_the_desktops_handler_and_a_scripts_does_not()
    {
        ProbeApp.Display();
        var probe = ProbeApp.Probe();
        var run = ProbeApp.NewRunFolder();
        var opened = StubBrowser.Install(run);
        await using var app = await ProbeApp.LaunchAsync(probe, ProbeApp.ProbeAppId, [("BRIDGE_PROBE_PLAN", "external-link")], runFolder: run);
        await app.WaitForAsync(Note + "external: ready$");

        XTest.ClickMiddle(ProbeApp.Display(), X11Frames.MainWindow(ProbeApp.Display(), app.Id));

        await app.WaitForAsync("Opening https://example.invalid/from-click with the desktop's handler");
        var limit = Stopwatch.StartNew();
        while (!File.Exists(opened) && limit.Elapsed < ProbeApp.Patience)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["https://example.invalid/from-click"], File.ReadAllLines(opened));
        await app.WaitForAsync(Note + "external: clicked$");

        // The script's own click on the same link before it: no window, nothing handed to the desktop.
        Assert.Single(Regex.Matches(app.Log(), "with the desktop's handler"));
        Assert.Equal(1, app.WindowCount());
        Record($"{Build()}: a user's click on an external link reached the stub handler in {limit.Elapsed.TotalMilliseconds:0} ms after the host's line");
        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task The_network_session_keeps_its_data_and_cache_in_the_folders_Spar_passed()
    {
        await using var app = await ProbeApp.StartAsync("");
        await app.WaitForAsync(Note + "plan done$");
        var root = app.AppFolder()!;
        var folders = await app.WaitForAsync(@"Browser data folder (?<data>\S+), cache (?<cache>\S+)$");

        Assert.Equal(Path.Combine(root, "WebKit"), folders.Groups["data"].Value);
        Assert.Equal(Path.Combine(root, "cache", "WebKit"), folders.Groups["cache"].Value);
        Assert.Equal(0, await app.CloseAsync());

        // Nothing of WebKit's outside the two folders, anywhere under the run's HOME and XDG folders.
        var written = Directory.EnumerateFiles(app.RunFolder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app.RunFolder, path))
            .ToList();
        var outside = written.Where(path => !path.StartsWith(Path.GetRelativePath(app.RunFolder, root) + "/", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(outside, path => path.Contains("webkit", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "WebKit")));
        var inRoot = written.Except(outside)
            .Select(path => string.Join('/', path.Split('/').Skip(2).Take(2)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        Record($"{Build()}: the run wrote {written.Count} files; outside the app's folder: [{string.Join(", ", outside.Select(Top).Distinct(StringComparer.Ordinal))}]; in it: [{string.Join(", ", inRoot)}]");
    }

    [Fact]
    public async Task A_profile_folder_the_app_chose_is_the_one_the_session_uses_and_a_relative_one_stops_the_start()
    {
        var run = ProbeApp.NewRunFolder();
        var chosen = Path.Combine(run, "chosen-profile");
        await using (var app = await ProbeApp.LaunchAsync(ProbeApp.Probe(), ProbeApp.ProbeAppId, [("BRIDGE_PROBE_PLAN", ""), ("BRIDGE_PROBE_PROFILE", chosen)], runFolder: run))
        {
            await app.WaitForAsync($"Browser data folder {Regex.Escape(chosen)}, cache ");
            await app.WaitForAsync(Note + "plan done$");
            Assert.Equal(0, await app.CloseAsync());
            Assert.NotEmpty(Directory.EnumerateFileSystemEntries(chosen));
            Assert.False(Directory.Exists(Path.Combine(app.AppFolder()!, "WebKit")), "The default folder was used too.");
        }

        // The app's start-up message, as on Windows: a Production build waits for it (answered through AT-SPI), a
        // Development build in the automated mode shows none.
        await using var relative = await ProbeApp.StartAsync("", ("BRIDGE_PROBE_PROFILE", "relative/profile"), ("SPAR_AUTOMATED", "1"));
        await relative.AnswerAsync("Close", TimeSpan.FromSeconds(15));
        Assert.Equal(70, await relative.WaitForExitAsync());
        Assert.Contains("The app's configuration stops its start", relative.Log(), StringComparison.Ordinal);
        Assert.Contains("is not an absolute path on a local drive", relative.Log(), StringComparison.Ordinal);
    }

    /// <summary>
    /// WebAssembly as the app declared it (M1c step 5; decision "WebAssembly off on Linux"): <c>JSC_useWasm</c> set
    /// before WebKit starts, replacing an inherited value of the other kind, absent by default and working with
    /// <c>csp.AllowWebAssembly()</c> (the probe's own build of it, <c>SPAR_LINUX_WASM_APP</c>), in the page and in its
    /// dedicated and shared workers, and again in the web process that replaces a lost one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebAssembly_follows_the_declaration_in_the_page_and_its_workers_and_after_a_lost_web_process(bool allowed)
    {
        var probe = allowed ? ProbeApp.WebAssemblyProbe() : ProbeApp.Probe();
        var inherited = allowed ? "false" : "true";
        await using var app = await ProbeApp.LaunchAsync(
            probe,
            ProbeApp.ProbeAppId,
            [("BRIDGE_PROBE_PLAN", "wasm,again-wasm"), ("JSC_useWasm", inherited), ("JSC_useWebAssembly", inherited)]);

        var first = await app.WaitForAsync(Note + "wasm: (?<json>.+)$");

        AssertWebAssembly(first, allowed);
        var log = app.Log();
        var value = allowed ? "true" : "false";
        Assert.Contains($"JSC_useWasm={value}, replacing the inherited JSC_useWasm={inherited}.", log, StringComparison.Ordinal);

        // The option's name before WebKitGTK 2.48: JavaScriptCore ignores it, and the host names it, as every JSC_ one:
        // removed from a Production build, kept by a Development one (M1c step 6).
        var development = BuildOf(probe) == "development";
        Assert.Matches(
            development
                ? @"WARN  Spar\.Host  \[[0-9a-f]{8}\] JSC_useWebAssembly in force \(a Development build keeps JavaScriptCore options\)"
                : @"WARN  Spar\.Host  \[[0-9a-f]{8}\] JSC_useWebAssembly ignored: a JavaScriptCore option",
            log);
        Assert.Equal(allowed, Regex.IsMatch(log, "Content-Security-Policy: .*script-src [^;]*'wasm-unsafe-eval'"));
        var webProcess = app.WebKitProcesses().First(process => process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal));
        var environment = ProbeApp.EnvironmentOf(webProcess.Id);
        Assert.Equal(value, environment["JSC_useWasm"]);
        Assert.Equal(development, environment.ContainsKey("JSC_useWebAssembly"));

        app.EndWebProcess();
        var again = await app.WaitForAsync(Note + @"wasm \(reloaded\): (?<json>.+)$");

        AssertWebAssembly(again, allowed);
        Assert.Equal(0, await app.CloseAsync());
        Record($"{BuildOf(probe)}, {(allowed ? "AllowWebAssembly()" : "default")}: {first.Groups["json"].Value}; after a lost web process {again.Groups["json"].Value}");
    }

    /// <summary>"Reload page" ends the web process through WebKit's API; the one it starts has the setting too.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebAssembly_follows_the_declaration_after_Reload_page_ends_the_web_process(bool allowed)
    {
        var probe = allowed ? ProbeApp.WebAssemblyProbe() : ProbeApp.Probe();
        await using var app = await ProbeApp.LaunchAsync(
            probe,
            ProbeApp.ProbeAppId,
            [("BRIDGE_PROBE_PLAN", "wasm,hang,again-wasm"), ("SPAR_DEV_HEARTBEAT_MS", "500"), ("SPAR_DEV_PROMPT_ANSWERS", "second@100")]);

        AssertWebAssembly(await app.WaitForAsync(Note + "wasm: (?<json>.+)$"), allowed);

        // A Development build answers by its script; a Production one through AT-SPI, after the 5 s heartbeat's alarm.
        await app.AnswerAsync("Reload page", TimeSpan.FromSeconds(40));
        await app.WaitForAsync("web process ended by the host", TimeSpan.FromSeconds(40));
        var again = await app.WaitForAsync(Note + @"wasm \(reloaded\): (?<json>.+)$");

        AssertWebAssembly(again, allowed);
        Assert.Equal(0, await app.CloseAsync());
        Record($"{BuildOf(probe)}, {(allowed ? "AllowWebAssembly()" : "default")}: after Reload page {again.Groups["json"].Value}");
    }

    private static async Task<(double Raised, (int Batches, int Read, int Refused, int Dropped) Totals, string Log)> FloodAsync(
        int count, (string Name, string? Value)[] variables)
    {
        await using var app = await ProbeApp.StartAsync($"distinct:{count}", variables);
        var raised = await app.WaitForAsync(Note + "distinct: raised \\d+ in (?<ms>[0-9.]+) ms$", TimeSpan.FromSeconds(180));
        Assert.Equal(0, await app.CloseAsync());
        var log = app.Log();
        return (double.Parse(raised.Groups["ms"].Value, CultureInfo.InvariantCulture), Totals(log), log);
    }

    /// <summary>The exit's totals line of the violation log; all zero when the run reported nothing.</summary>
    private static (int Batches, int Read, int Refused, int Dropped) Totals(string log)
    {
        var line = Regex.Match(
            log, @"Content Security Policy reports in this run: (?<b>\d+) listener batches, (?<r>\d+) report-uri reports read, (?<f>\d+) refused unread \(rate or size\), (?<d>\d+) dropped");
        Assert.True(line.Success, $"No totals line.\n{log}");
        int Number(string name) => int.Parse(line.Groups[name].Value, CultureInfo.InvariantCulture);
        return (Number("b"), Number("r"), Number("f"), Number("d"));
    }

    /// <summary>
    /// The page's, the dedicated worker's and the shared worker's answer: no global by default, a module that runs with
    /// the opt-in. WebKitGTK registers no service worker from <c>spar://app</c>: should that change, a service worker
    /// needs its own check.
    /// </summary>
    private static void AssertWebAssembly(Match note, bool allowed)
    {
        using var seen = JsonDocument.Parse(note.Groups["json"].Value);
        var expected = allowed ? "object, add(2, 3) = 5" : "undefined";
        foreach (var place in new[] { "page", "worker", "shared" })
        {
            Assert.Equal(expected, seen.RootElement.GetProperty(place).GetString());
        }

        Assert.StartsWith(
            "register refused (TypeError: serviceWorker.register() must be called with a script URL whose protocol is either HTTP or HTTPS)",
            seen.RootElement.GetProperty("service").GetString(),
            StringComparison.Ordinal);
    }

    private static string BuildOf(string probe) => ProbeApp.IsDevelopment(probe) ? "development" : "production";

    private static string Top(string path) => string.Join('/', path.Split('/').Take(2));

    private static string Build() => ProbeApp.IsDevelopmentProbe() ? "development" : "production";

    private static string ResultsFolder() => Results.Folder(".");

    private static void Record(string line) =>
        File.AppendAllLines(Path.Combine(ResultsFolder(), "linux-page-security.txt"), [string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss} {line}")]);

    /// <summary>
    /// A handler for <c>http</c>, <c>https</c> and <c>mailto</c> in a run's own XDG folders: a script that appends the
    /// address it gets to a file. Before the app starts, GIO's own tool is asked, with the run's folders, which
    /// handler opens <c>https</c>; a run where it is anything else stops there, so no real browser can open.
    /// </summary>
    private static class StubBrowser
    {
        public static string Install(string run)
        {
            var applications = Path.Combine(run, "data", "applications");
            var config = Path.Combine(run, "config");
            Directory.CreateDirectory(applications);
            Directory.CreateDirectory(config);
            var opened = Path.Combine(run, "opened.txt");
            var script = Path.Combine(run, "stub-browser.sh");
            File.WriteAllText(script, $"#!/bin/sh\nprintf '%s\\n' \"$1\" >> '{opened}'\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(applications, "spar-stub-browser.desktop"), $"""
                [Desktop Entry]
                Type=Application
                Name=Spar test stub browser
                Exec={script} %u
                NoDisplay=true
                MimeType=x-scheme-handler/http;x-scheme-handler/https;x-scheme-handler/mailto;

                """);
            File.WriteAllText(Path.Combine(config, "mimeapps.list"), """
                [Default Applications]
                x-scheme-handler/http=spar-stub-browser.desktop
                x-scheme-handler/https=spar-stub-browser.desktop
                x-scheme-handler/mailto=spar-stub-browser.desktop

                """);

            var check = new ProcessStartInfo("gio", ["mime", "x-scheme-handler/https"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            check.Environment["HOME"] = Path.Combine(run, "home");
            check.Environment["XDG_DATA_HOME"] = Path.Combine(run, "data");
            check.Environment["XDG_CONFIG_HOME"] = config;
            using var gio = Process.Start(check)!;
            var answer = gio.StandardOutput.ReadToEnd();
            gio.WaitForExit();
            Assert.True(
                Regex.IsMatch(answer, @"^Default application for .x-scheme-handler/https.: spar-stub-browser\.desktop$", RegexOptions.Multiline),
                $"GIO does not pick the stub handler for https here, so the test stops before any click:\n{answer}");
            return opened;
        }
    }

    /// <summary>
    /// A site of the test's own on loopback, whose address the probe declares for frames (<c>http://127.0.0.1:*</c>):
    /// its page, and the same address with <c>?redirect=1</c> answered with a redirect to another origin.
    /// </summary>
    private sealed class LoopbackSite : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serving;

        private LoopbackSite(int port)
        {
            var origin = $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}";
            Page = origin + "/frame.html";
            _listener.Prefixes.Add(origin + "/");
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public string Page { get; }

        public ConcurrentQueue<string> Requests { get; } = new();

        public static LoopbackSite Start()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return new LoopbackSite(port);
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

                Requests.Enqueue(context.Request.Url?.PathAndQuery ?? "");
                if (context.Request.QueryString["redirect"] is not null)
                {
                    context.Response.StatusCode = 302;
                    context.Response.RedirectLocation = "http://example.invalid/after-redirect";
                    context.Response.Close();
                    continue;
                }

                var body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=\"utf-8\"><title>Declared frame</title><p>declared</p>");
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }
    }
}
