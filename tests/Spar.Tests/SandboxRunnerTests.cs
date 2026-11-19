using System.IO.Compression;
using System.Xml.Linq;
using Spar.Testing;

namespace Spar.Tests;

/// <summary>
/// The Windows Sandbox runner's pure parts (Spar.Testing, step 8c), without a sandbox: the configuration it starts the
/// guest with, the guest script and its quoting, the reader of the guest's streamed results, the parsing of <c>wsb</c>'s
/// output, the allow gate, the guest check, and real input refusing on this machine. The runner itself runs in
/// <c>tests/Spar.SandboxTests</c>.
/// </summary>
public sealed class SandboxRunnerTests
{
    [Fact]
    public void The_configuration_turns_everything_off_and_maps_the_input_read_only()
    {
        var xml = XElement.Parse(GuestScript.Configuration(new SandboxFeatures(), @"E:\runs\a&b\in", @"E:\runs\a&b\out"));

        foreach (var name in new[] { "Networking", "ClipboardRedirection", "PrinterRedirection", "VideoInput", "AudioInput" })
        {
            Assert.Equal("Disable", xml.Element(name)?.Value);
        }

        var folders = xml.Element("MappedFolders")!.Elements("MappedFolder").ToList();
        Assert.Equal(2, folders.Count);
        Assert.Equal((@"E:\runs\a&b\in", @"C:\spar\in", "true"), Folder(folders[0]));
        Assert.Equal((@"E:\runs\a&b\out", @"C:\spar\out", "false"), Folder(folders[1]));
        Assert.Null(xml.Element("LogonCommand"));
    }

    [Fact]
    public void A_feature_the_run_asks_for_is_turned_on_and_only_that_one()
    {
        var xml = XElement.Parse(GuestScript.Configuration(new SandboxFeatures { Networking = true }, @"C:\in", @"C:\out"));

        Assert.Equal("Enable", xml.Element("Networking")?.Value);
        Assert.Equal("Disable", xml.Element("ClipboardRedirection")?.Value);
    }

    [Fact]
    public void The_guest_script_marks_the_run_names_the_folders_and_writes_the_exit_code_last()
    {
        var job = new SandboxJob("window-tests/System", @"C:\payload", "Spar.WindowTests.exe", SandboxLane.System, TimeSpan.FromMinutes(1))
        {
            Arguments = ["-trait", "Sandbox=System", "-explicit", "on"],
            Environment = new Dictionary<string, string> { ["EXTRA"] = "one two" },
        };

        var script = GuestScript.Job(job, "j01-abcdef", "run123");

        Assert.Contains("set \"SPAR_SANDBOX_RUN=run123\"", script, StringComparison.Ordinal);
        Assert.Contains(@"set ""SPAR_TEST_ARTIFACTS=C:\spar\work\j01-abcdef\artifacts""", script, StringComparison.Ordinal);
        Assert.Contains(@"set ""SPAR_TEST_RESULTS=%OUT%\test-results""", script, StringComparison.Ordinal);
        Assert.Contains("set \"EXTRA=one two\"", script, StringComparison.Ordinal);
        Assert.Contains(
            @"""C:\spar\work\j01-abcdef\payload\Spar.WindowTests.exe"" -trait Sandbox=System -explicit on -noColor -noLogo -reporter json " +
            @"-result-trx %OUT%\results.trx -result-xml %OUT%\results.xml > ""%OUT%\console.log"" 2> ""%OUT%\error.log""",
            script,
            StringComparison.Ordinal);
        Assert.True(
            script.LastIndexOf("exitcode.txt", StringComparison.Ordinal) > script.IndexOf("console.log", StringComparison.Ordinal),
            "The exit code is moved into place after the run.");
        Assert.DoesNotContain("\n", script.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void The_guest_script_refuses_what_cmd_would_read_as_syntax()
    {
        static SandboxJob With(string executable, params string[] arguments) =>
            new("bad", @"C:\payload", executable, SandboxLane.System, TimeSpan.FromMinutes(1)) { Arguments = arguments };

        Assert.Throws<ArgumentException>(() => GuestScript.Job(With("a&b.exe"), "j01-abcdef", "run"));
        Assert.Throws<ArgumentException>(() => GuestScript.Job(With("Tests.exe", "%PATH%"), "j01-abcdef", "run"));
        Assert.Throws<ArgumentException>(() => GuestScript.Job(With("Tests.exe", "say \"hi\""), "j01-abcdef", "run"));
        Assert.Throws<ArgumentException>(() => GuestScript.Job(With("Tests.exe", "a|b"), "j01-abcdef", "run"));
        Assert.Equal("\"a b\"", GuestScript.Quote("a b"));
        Assert.Equal("-trait", GuestScript.Quote("-trait"));
        Assert.Equal(@"%OUT%\results.trx", GuestScript.Quote(@"%OUT%\results.trx"));
    }

    [Fact]
    public void The_results_reader_keeps_each_finished_test_and_marks_the_unfinished_one()
    {
        var text = string.Join('\n',
            """{"$type":"test-starting","TestUniqueID":"a","TestDisplayName":"T.Passes"}""",
            """{"$type":"test-output","TestUniqueID":"a","Output":"hello"}""",
            """{"$type":"test-passed","TestUniqueID":"a","ExecutionTime":0.25}""",
            """{"$type":"test-starting","TestUniqueID":"b","TestDisplayName":"T.Fails"}""",
            """{"$type":"test-failed","TestUniqueID":"b","ExecutionTime":1,"ExceptionTypes":["Xunit.Sdk.TrueException"],"Messages":["Assert.True() Failure"],"StackTraces":["at T.Fails()"]}""",
            """{"$type":"test-starting","TestUniqueID":"c","TestDisplayName":"T.Skips"}""",
            """{"$type":"test-skipped","TestUniqueID":"c","Reason":"Not here"}""",
            "not json at all",
            """{"$type":"test-starting","TestUniqueID":"d","TestDisplayName":"T.Hangs"}""",
            """{"$type":"test-passed","TestUniqueID":"d","Exec""");

        var results = GuestTestResults.Parse(text);

        Assert.Equal(["T.Passes", "T.Fails", "T.Skips", "T.Hangs"], results.Select(result => result.DisplayName));
        Assert.Equal([GuestOutcome.Passed, GuestOutcome.Failed, GuestOutcome.Skipped, GuestOutcome.Unfinished], results.Select(result => result.Outcome));
        Assert.Equal(TimeSpan.FromSeconds(0.25), results[0].Duration);
        Assert.Equal("hello", results[0].Output);
        Assert.Equal("Xunit.Sdk.TrueException: Assert.True() Failure\nat T.Fails()", results[1].Message);
        Assert.Equal("Not here", results[2].Message);
    }

    [Fact]
    public void Wsb_output_is_read_as_its_raw_json()
    {
        Assert.Equal(["5e1"], Wsb.ParseList("""{ "WindowsSandboxEnvironments": [ { "Id": "5e1" } ] }"""));
        Assert.Empty(Wsb.ParseList("""{"WindowsSandboxEnvironments":[]}""")!);
        Assert.Null(Wsb.ParseList("Application cannot be run more than once (0x800401F6)"));

        Assert.Equal(3, new WsbResult(0, "{\r\n  \"ExitCode\": 3\r\n}", "", TimeSpan.Zero, false).GuestExitCode);
        Assert.Null(new WsbResult(1, "The Windows Sandbox ID provided was not found.", "", TimeSpan.Zero, false).GuestExitCode);
    }

    [Fact]
    public void The_user_session_lane_runs_only_when_the_run_says_allow()
    {
        Assert.True(SandboxDesktopGate.IsAllowedValue("allow"));
        Assert.True(SandboxDesktopGate.IsAllowedValue(" Allow "));
        Assert.False(SandboxDesktopGate.IsAllowedValue("yes"));
        Assert.False(SandboxDesktopGate.IsAllowedValue("1"));
        Assert.False(SandboxDesktopGate.IsAllowedValue(null));
        Assert.Contains(SandboxDesktopGate.Variable + "=allow", SandboxDesktopGate.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runners_guest_needs_the_run_id_the_marker_with_the_same_id_and_for_input_the_guests_user()
    {
        static string? Marker(string path) => path == SandboxGuest.MarkerPath ? "run123\r\n" : null;
        static string? None(string path) => null;

        Assert.Null(SandboxGuest.Refusal("run123", null, Marker));
        Assert.Null(SandboxGuest.Refusal("run123", "WDAGUtilityAccount", Marker));
        Assert.Contains("is not set", SandboxGuest.Refusal(null, null, Marker), StringComparison.Ordinal);
        Assert.Contains("is missing", SandboxGuest.Refusal("run123", null, None), StringComparison.Ordinal);
        Assert.Contains("another run", SandboxGuest.Refusal("run999", null, Marker), StringComparison.Ordinal);
        Assert.Contains("not as the guest's user", SandboxGuest.Refusal("run123", "SYSTEM", Marker), StringComparison.Ordinal);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763")]
    public void Real_input_the_screen_and_a_second_window_are_refused_on_this_machine()
    {
        Assert.SkipUnless(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763), "Real input is Windows only.");
        Assert.SkipWhen(SandboxGuest.IsUserSession, "This run is inside a runner's guest, where real input is allowed.");

        var click = Assert.Throws<InvalidOperationException>(() => RealInput.Click(10, 10));
        Assert.Contains("Nothing was sent", click.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => RealInput.Press("F12"));
        Assert.Throws<InvalidOperationException>(() => RealInput.Type("x"));
        Assert.Throws<InvalidOperationException>(() => ScreenCapture.Save(Path.Combine(Path.GetTempPath(), "spar-never.png")));
        Assert.Throws<InvalidOperationException>(() => OtherWindow.Show(new ScreenRect(0, 0, 100, 100)));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "spar-never.png")));
    }

    [Fact]
    public void Keys_go_down_as_a_keyboard_sends_them()
    {
        var (modifiers, key) = KeyboardLayout.Parse("Control+Shift+I");
        Assert.Equal([(ushort)0x11, (ushort)0x10], RealInput.Held(modifiers));
        Assert.Equal(0x49, key.VirtualKey);
        Assert.Equal([(ushort)0x11, (ushort)0x10, (ushort)0x12], RealInput.Held(KeyboardLayout.Parse("Control+Shift+Alt+F12").Modifiers));
        Assert.True(RealInput.IsExtended(0x25), "ArrowLeft is an extended key.");
        Assert.True(RealInput.IsExtended(0x2E), "Delete is an extended key.");
        Assert.False(RealInput.IsExtended(0x7B), "F12 is not.");
        Assert.Equal(187, KeyboardLayout.Parse("Control+=").Key.VirtualKey);
    }

    [Fact]
    public void A_screenshot_is_a_png_whose_rows_read_back()
    {
        byte[] bgra = [10, 20, 30, 255, 40, 50, 60, 255];

        var png = ScreenCapture.Png(2, 1, bgra);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal(new byte[] { 0xAE, 0x42, 0x60, 0x82 }, png[^4..]);
        var length = (png[33] << 24) | (png[34] << 16) | (png[35] << 8) | png[36];
        using var zlib = new ZLibStream(new MemoryStream(png, 41, length), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal(new byte[] { 0, 30, 20, 10, 60, 50, 40 }, raw.ToArray());
    }

    private static (string Host, string Guest, string ReadOnly) Folder(XElement folder) =>
        (folder.Element("HostFolder")!.Value, folder.Element("SandboxFolder")!.Value, folder.Element("ReadOnly")!.Value);
}
