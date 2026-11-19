using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Spar.Tests;

/// <summary>
/// The Linux host's start-up (plan M1c, build step 2), on the bridge probe's executable under the runner's X server:
/// every exit code forced and observed (a library, a helper or <c>bwrap</c> hidden in a bubblewrap mount namespace, the
/// sandbox denied by one that forbids user namespaces, no display, a raised WebKitGTK minimum in a Development build),
/// the message each shows and closes, single instance (a hand-over and a stopped first instance), the software-GPU
/// default in the web processes' environment, and the theme. The times go to
/// <c>artifacts/test-results/Spar.Tests/linux-startup.txt</c>; they are recorded, not asserted.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(LinuxWindowsCollection))]
public sealed class LinuxStartupTests
{
    private const string LibraryFolder = "/usr/lib/x86_64-linux-gnu";

    [Fact]
    public async Task No_display_is_exit_6_with_its_line_and_the_apps_hook()
    {
        await using var app = LinuxAppRun.Start("", ("DISPLAY", null), ("BRIDGE_PROBE_OWN_STARTUP_MESSAGE", "1"));

        Assert.Equal(6, await app.WaitForExitAsync());
        Record("no display", app);
        Assert.Contains("Start-up problem NoGraphicalSession: Display.", app.Output, StringComparison.Ordinal);
        Assert.Contains("probe: own start-up message for NoGraphicalSession, Display", app.Output, StringComparison.Ordinal);
        Assert.Contains("Start-up problem NoGraphicalSession", app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_display_nobody_serves_is_exit_6()
    {
        await using var app = LinuxAppRun.Start("", ("DISPLAY", ":59"));

        Assert.Equal(6, await app.WaitForExitAsync());
        Record("display not served", app);
        Assert.Contains("GTK could not open the display :59", app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_WebKitGTK_older_than_required_is_exit_4_with_a_message()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "The raised minimum is a Development build's variable.");
        await using var app = LinuxAppRun.Start("", ("SPAR_DEV_REQUIRE_WEBKITGTK", "99.0"));

        await ShownThenClosedAsync(app, 4, "too old");
        Assert.Matches(@"Start-up problem TooOld: WebKitGTK 6\.0 \(found 2\.\d+\.\d+, required 99\.0\.0\)", app.Log());
    }

    [Fact]
    public async Task A_missing_WebKitGTK_is_exit_3_with_a_message()
    {
        await using var app = Hidden(["--ro-bind", "/dev/null", Real(Path.Combine(LibraryFolder, "libwebkitgtk-6.0.so.4"))]);

        await ShownThenClosedAsync(app, 3, "WebKitGTK missing");
        Assert.Contains("Start-up problem Missing: WebKitGTK 6.0 (required 2.48.0).", app.Log(), StringComparison.Ordinal);
        Assert.Contains("sudo apt install libwebkitgtk-6.0-4", app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_GTK_is_exit_3_without_a_GTK_message()
    {
        await using var app = Hidden(["--ro-bind", "/dev/null", Real(Path.Combine(LibraryFolder, "libgtk-4.so.1"))]);

        Assert.Equal(3, await app.WaitForExitAsync());
        Record("GTK missing (no message program here)", app);
        Assert.Contains("Start-up problem Missing: GTK 4.", app.Log(), StringComparison.Ordinal);
        Assert.Matches("The start-up message was shown with|No start-up message could be shown", app.Log());
    }

    [Fact]
    public async Task Missing_WebKit_helpers_are_exit_5_with_a_message()
    {
        await using var app = Hidden(["--tmpfs", Path.Combine(LibraryFolder, "webkitgtk-6.0")]);

        await ShownThenClosedAsync(app, 5, "helpers missing");
        Assert.Contains("Start-up problem Broken: WebKitGTK 6.0.", app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_bwrap_is_exit_3_with_a_message()
    {
        await using var app = Hidden(["--ro-bind", "/dev/null", "/usr/bin/bwrap"]);

        await ShownThenClosedAsync(app, 3, "bwrap missing");
        Assert.Contains("Start-up problem Missing: bubblewrap.", app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sandbox_the_system_denies_is_exit_5_with_a_message()
    {
        await using var app = Hidden(["--unshare-user", "--disable-userns"]);

        await ShownThenClosedAsync(app, 5, "sandbox denied");
        Assert.Contains("Start-up problem Broken: Web content sandbox.", app.Log(), StringComparison.Ordinal);
        Assert.Matches(@"Sandbox trial: bwrap exit [1-9]\d* in [0-9.]+ ms\.", app.Log());
    }

    [Fact]
    public async Task A_second_instance_hands_its_arguments_over_and_exits_0()
    {
        await using var first = LinuxAppRun.Start("");
        await first.WaitForAsync(@"ui-ready at");

        var clock = Stopwatch.StartNew();
        await using var second = LinuxAppRun.Launch(
            LinuxAppRun.Probe(), LinuxAppRun.ProbeAppId, [("DESKTOP_STARTUP_ID", "spar-test-token")], first.RunFolder, arguments: ["--recipe", "nightly.sqlrecipe"]);
        Assert.Equal(0, await second.WaitForExitAsync());
        clock.Stop();

        await first.WaitForAsync(@"probe relaunch: \[--recipe \| nightly\.sqlrecipe\]");
        Assert.Contains("Relaunch with 2 arguments: the window was presented with its activation token.", first.Log(), StringComparison.Ordinal);
        Assert.Contains("Handed this launch to the running instance", second.Log(), StringComparison.Ordinal);
        RecordLine(string.Create(CultureInfo.InvariantCulture, $"second instance: exit 0 in {clock.Elapsed.TotalMilliseconds:0.0} ms from the test; {HandOff(second.Output)}"));
        Assert.Equal(0, await first.CloseAsync());
    }

    [Fact]
    public async Task A_stopped_first_instance_gives_the_second_exit_8_after_2_seconds()
    {
        await using var first = LinuxAppRun.Start("");
        await first.WaitForAsync(@"ui-ready at");
        Signal("STOP", first.Id);
        try
        {
            var clock = Stopwatch.StartNew();
            await using var second = LinuxAppRun.Launch(
                LinuxAppRun.Probe(), LinuxAppRun.ProbeAppId, [("SPAR_AUTOMATED", "1")], first.RunFolder, arguments: ["late"]);
            // A Production build has no automated mode: it shows the message, which the test closes.
            var development = LinuxAppRun.IsDevelopmentProbe();
            var code = development ? await second.WaitForExitAsync() : -1;
            var shown = development ? (TimeSpan?)null : await second.WaitForWindowAsync();
            clock.Stop();
            Assert.Equal(8, development ? code : await second.CloseAsync());
            Assert.Contains("did not answer within 2 seconds", second.Log(), StringComparison.Ordinal);
            Assert.InRange(clock.Elapsed.TotalMilliseconds, 1_900, 5_000);
            RecordLine(string.Create(CultureInfo.InvariantCulture, $"stopped first instance: the second {(shown is null ? "exits 8" : "shows its exit-8 message")} {clock.Elapsed.TotalMilliseconds:0.0} ms after the test launched it"));
        }
        finally
        {
            Signal("CONT", first.Id);
        }

        // Resumed, the first finds a relaunch older than its sender waits for, and drops it.
        await first.WaitForAsync(@"a relaunch that waited \d+ ms, longer than its sender waits, was dropped");
        Assert.DoesNotContain("probe relaunch: [late]", first.Log(), StringComparison.Ordinal);
        Assert.Equal(0, await first.CloseAsync());
    }

    [Theory]
    [InlineData(null, null, true, "1", "cairo")]
    [InlineData("Always", null, false, null, null)]
    [InlineData("Never", null, true, "1", "cairo")]
    [InlineData(null, "ngl", true, "1", "ngl")]
    public async Task The_software_GPU_default_reaches_the_web_processes(
        string? option, string? userRenderer, bool off, string? dmabuf, string? renderer)
    {
        await using var app = LinuxAppRun.Start("", ("BRIDGE_PROBE_ACCELERATION", option), ("GSK_RENDERER", userRenderer));
        await app.WaitForAsync(@"ui-ready at");

        var line = Regex.Match(app.Log(), @"Hardware acceleration (?<option>\w+): (?<state>on|off), GPU (?<gpu>.*), checked in (?<ms>[0-9.]+) ms with (?<threads>\d+) threads; (?<applied>.*)\.$", RegexOptions.Multiline);
        Assert.True(line.Success, app.Log());
        Assert.Equal(option ?? "Auto", line.Groups["option"].Value);
        Assert.Equal(off ? "off" : "on", line.Groups["state"].Value);
        var page = Assert.Single(app.WebKitProcesses(), process => process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal));
        var environment = LinuxAppRun.EnvironmentOf(page.Id);
        Assert.Equal(dmabuf, environment.GetValueOrDefault("WEBKIT_DISABLE_DMABUF_RENDERER"));
        Assert.Equal(renderer, environment.GetValueOrDefault("GSK_RENDERER"));
        RecordLine($"gpu {option ?? "Auto"}{(userRenderer is null ? "" : $" with GSK_RENDERER={userRenderer}")}: {line.Value[line.Value.IndexOf("Hardware", StringComparison.Ordinal)..]}");
        Assert.Equal(0, await app.CloseAsync());
    }

    /// <summary>
    /// Light and dark in turn, five times each, so every window opens where the other scheme's window was just shown:
    /// what the screen showed before cannot pass for the right colour.
    /// </summary>
    [Fact]
    public async Task The_Notebooks_window_has_the_saved_schemes_colour_from_its_first_frame()
    {
        var notebook = Environment.GetEnvironmentVariable("SPAR_LINUX_NOTEBOOK");
        Assert.SkipWhen(string.IsNullOrEmpty(notebook), "Needs SPAR_LINUX_NOTEBOOK, the Notebook's published executable.");
        for (var run = 0; run < 10; run++)
        {
            var dark = run % 2 == 1;
            await SampleFirstFramesAsync(notebook!, dark ? "dark" : "light", dark ? 0x1D1F24u : 0xFFFFFFu);
        }
    }

    private static async Task SampleFirstFramesAsync(string notebook, string scheme, uint colour)
    {
        var display = LinuxAppRun.Display();
        var run = Path.Combine(TestFolders.Artifacts(), "linux-homes", "host-tests", Guid.NewGuid().ToString("N")[..12]);
        var dataRoot = Path.Combine(run, "data", "com.example.Notebook");
        Directory.CreateDirectory(dataRoot);
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "color-scheme"), scheme, TestContext.Current.CancellationToken);

        using var stop = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        await using var app = LinuxAppRun.Launch(notebook, "com.example.Notebook", [], run);
        var sampling = Task.Factory.StartNew(() => X11Frames.Sample(display, app.Id, clock, stop.Token), TaskCreationOptions.LongRunning);
        await app.WaitForAsync(@"ui-ready at");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        var samples = await sampling;

        Assert.Contains($"Theme {scheme}, #{colour:x6}, from the app's saved choice.", app.Log(), StringComparison.Ordinal);
        Assert.NotEmpty(samples);
        var wrong = samples.Where(sample => scheme == "dark" ? Luminance(sample.Colour) > 0.5 : Luminance(sample.Colour) < 0.5).ToList();
        var colours = samples.GroupBy(sample => sample.Colour).Select(group => $"#{group.Key:x6} ×{group.Count()} from {group.First().Milliseconds:0} ms");
        RecordLine(string.Create(CultureInfo.InvariantCulture, $"theme {scheme}: {samples.Count} samples from {samples[0].Milliseconds:0} ms, {wrong.Count} of the other scheme; {string.Join(", ", colours)}"));
        Assert.Empty(wrong);
        Assert.Equal(colour, samples[0].Colour);
        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task Without_a_portal_GTKs_dark_preference_makes_the_window_dark()
    {
        var run = Path.Combine(TestFolders.Artifacts(), "linux-homes", "host-tests", Guid.NewGuid().ToString("N")[..12]);
        var settings = Path.Combine(run, "config", "gtk-4.0");
        Directory.CreateDirectory(settings);
        await File.WriteAllTextAsync(Path.Combine(settings, "settings.ini"), "[Settings]\ngtk-application-prefer-dark-theme=1\n", TestContext.Current.CancellationToken);

        await using var app = LinuxAppRun.Launch(LinuxAppRun.Probe(), LinuxAppRun.ProbeAppId, [("BRIDGE_PROBE_PLAN", "")], run);
        await app.WaitForAsync(@"ui-ready at");

        Assert.Matches(@"Theme dark, #1f1f1f, from no Settings portal \(.*\); GTK's settings \(prefer-dark True, theme .*\)\.", app.Log());
        Assert.Equal(0, await app.CloseAsync());
    }

    [Fact]
    public async Task The_portals_scheme_decides_and_its_change_repaints_the_window()
    {
        Assert.SkipUnless(File.Exists("/usr/bin/dbus-daemon") && File.Exists("/usr/bin/python3"), "Needs dbus-daemon and python3 with GObject introspection.");
        using var bus = PrivateBus.Start();
        using var portal = FakePortal.Start(bus.Address, scheme: 1);

        await using var app = LinuxAppRun.Start("", ("DBUS_SESSION_BUS_ADDRESS", bus.Address));
        await app.WaitForAsync(@"ui-ready at");
        Assert.Contains("Theme dark, #1f1f1f, from the Settings portal.", app.Log(), StringComparison.Ordinal);

        portal.Change(2);
        await app.WaitForAsync("Theme changed to light by the system.");
        Assert.Equal(0, await app.CloseAsync());
    }

    /// <summary>The probe in a bubblewrap mount namespace where <paramref name="change"/> hides or denies something.</summary>
    private static LinuxAppRun Hidden(string[] change) =>
        LinuxAppRun.Launch(LinuxAppRun.Probe(), LinuxAppRun.ProbeAppId, [("BRIDGE_PROBE_PLAN", "")], wrapper: ["bwrap", "--dev-bind", "/", "/", .. change, "--"]);

    /// <summary>Waits for the start-up message (a window of the process), records when it showed, closes it, checks the code.</summary>
    private static async Task ShownThenClosedAsync(LinuxAppRun app, int exitCode, string label)
    {
        var shown = await app.WaitForWindowAsync();
        await app.WaitForAsync(@"ERROR .*Start-up problem");
        Assert.Equal(exitCode, await app.CloseAsync());
        Record(label, app, shown);
    }

    private static void Record(string label, LinuxAppRun app, TimeSpan? shown = null)
    {
        var log = app.Log();
        var problem = Regex.Match(log, @"^(?<time>\S+) ERROR .*Start-up problem", RegexOptions.Multiline);
        var reported = problem.Success ? $"reported {(LinuxAppRun.TimeOf(problem) - app.Launched).TotalMilliseconds:0.0} ms after launch" : "no log line";
        var message = shown is { } window ? $", message shown at {window.TotalMilliseconds:0.0} ms" : "";
        RecordLine(string.Create(CultureInfo.InvariantCulture, $"{label}: exit {app.ExitCode}, {reported}{message}"));
    }

    /// <summary>When the second instance handed over, from its standard error (the host's time since its start, 0.1 ms steps).</summary>
    private static string HandOff(string output) =>
        Regex.Match(output, @"\+(?<ms>[0-9.]+) ms: \[[0-9a-f]{8}\] Handed this launch") is { Success: true } handed
            ? $"handed over {handed.Groups["ms"].Value} ms after its host started"
            : "hand-over time not in its output";

    private static void RecordLine(string line)
    {
        var results = Path.Combine(TestFolders.Artifacts(), "test-results", "Spar.Tests");
        Directory.CreateDirectory(results);
        File.AppendAllLines(Path.Combine(results, "linux-startup.txt"), [string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss} {line}")]);
    }

    private static double Luminance(uint colour) =>
        ((0.2126 * ((colour >> 16) & 0xFF)) + (0.7152 * ((colour >> 8) & 0xFF)) + (0.0722 * (colour & 0xFF))) / 255;


    /// <summary>A library's real file, for a bind mount over it.</summary>
    private static string Real(string path)
    {
        Assert.SkipUnless(File.Exists(path), $"{path} is not on this system.");
        return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
    }

    private static void Signal(string signal, int process)
    {
        using var kill = Process.Start("kill", ["-" + signal, process.ToString(CultureInfo.InvariantCulture)]);
        kill.WaitForExit();
    }

    /// <summary>A D-Bus session bus of the test's own, so a fake portal can own the portal's name.</summary>
    private sealed class PrivateBus : IDisposable
    {
        private readonly Process _daemon;

        private PrivateBus(Process daemon, string address)
        {
            _daemon = daemon;
            Address = address;
        }

        public string Address { get; }

        public static PrivateBus Start()
        {
            var daemon = Process.Start(new ProcessStartInfo("dbus-daemon", ["--session", "--nofork", "--print-address=1"])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            var address = daemon.StandardOutput.ReadLine();
            Assert.False(string.IsNullOrEmpty(address), "dbus-daemon gave no address.");
            return new PrivateBus(daemon, address!);
        }

        public void Dispose()
        {
            _daemon.Kill();
            _daemon.WaitForExit();
            _daemon.Dispose();
        }
    }

    /// <summary>
    /// The XDG Settings portal's <c>color-scheme</c>, served by a Python script on the private bus: <c>ReadOne</c> and
    /// <c>Read</c> answer the current value, and a line <c>set N</c> on its input changes it and emits <c>SettingChanged</c>.
    /// </summary>
    private sealed class FakePortal : IDisposable
    {
        private const string Script = """
            import sys
            from gi.repository import Gio, GLib
            XML = '<node><interface name="org.freedesktop.portal.Settings"><method name="ReadOne"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/></method><method name="Read"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/></method><signal name="SettingChanged"><arg type="s"/><arg type="s"/><arg type="v"/></signal></interface></node>'
            PATH = '/org/freedesktop/portal/desktop'
            IFACE = 'org.freedesktop.portal.Settings'
            state = {'scheme': int(sys.argv[1]), 'conn': None}
            info = Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0]
            def call(conn, sender, path, iface, method, params, invocation):
                value = GLib.Variant('u', state['scheme'])
                if method == 'Read':
                    value = GLib.Variant('v', value)
                invocation.return_value(GLib.Variant('(v)', (value,)))
            def bus_acquired(conn, name):
                state['conn'] = conn
                conn.register_object(PATH, info, call, None, None)
            def name_acquired(conn, name):
                print('ready', flush=True)
            def line(source, condition):
                text = sys.stdin.readline()
                if not text:
                    loop.quit()
                    return False
                state['scheme'] = int(text.split()[1])
                state['conn'].emit_signal(None, PATH, IFACE, 'SettingChanged', GLib.Variant('(ssv)', ('org.freedesktop.appearance', 'color-scheme', GLib.Variant('u', state['scheme']))))
                state['conn'].flush_sync(None)
                print('changed', flush=True)
                return True
            Gio.bus_own_name(Gio.BusType.SESSION, 'org.freedesktop.portal.Desktop', Gio.BusNameOwnerFlags.NONE, bus_acquired, name_acquired, None)
            GLib.io_add_watch(sys.stdin, GLib.IO_IN | GLib.IO_HUP, line)
            loop = GLib.MainLoop()
            loop.run()
            """;

        private readonly Process _python;

        private FakePortal(Process python) => _python = python;

        public static FakePortal Start(string address, int scheme)
        {
            var start = new ProcessStartInfo("python3", ["-I", "-c", Script, scheme.ToString(CultureInfo.InvariantCulture)])
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            start.Environment["DBUS_SESSION_BUS_ADDRESS"] = address;
            var python = Process.Start(start)!;
            Assert.Equal("ready", python.StandardOutput.ReadLine());
            return new FakePortal(python);
        }

        public void Change(int scheme)
        {
            _python.StandardInput.WriteLine(string.Create(CultureInfo.InvariantCulture, $"set {scheme}"));
            _python.StandardInput.Flush();
            Assert.Equal("changed", _python.StandardOutput.ReadLine());
        }

        public void Dispose()
        {
            _python.StandardInput.Close();
            if (!_python.WaitForExit(2_000))
            {
                _python.Kill();
            }

            _python.Dispose();
        }
    }
}
