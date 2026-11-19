using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// The runner's SYSTEM lane, from inside the guest (no window, no user session): the guest is marked as the runner's,
/// the folders a test writes to are the ones the runner named, the network is off, and real input and the screen are
/// refused here, as everywhere but the user session.
/// </summary>
[Trait(SandboxLane.Trait, "System")]
public sealed class SystemLaneTests
{
    [Fact(Explicit = true)]
    public void The_runner_names_the_folders_and_marks_the_guest()
    {
        SandboxLane.SkipUnlessGuest();

        Assert.True(SandboxGuest.IsRunnerGuest);
        Assert.False(SandboxGuest.IsUserSession, $"The SYSTEM lane runs as {Environment.UserName}, not as the guest's user.");

        var artifacts = Environment.GetEnvironmentVariable(TestArtifacts.ArtifactsVariable);
        Assert.Equal(artifacts, TestArtifacts.Root);
        Assert.StartsWith(@"C:\spar\work\", artifacts, StringComparison.OrdinalIgnoreCase);

        var results = TestArtifacts.Results("Spar.WindowTests");
        Assert.StartsWith(@"C:\spar\out\", results, StringComparison.OrdinalIgnoreCase);

        var root = TestDataRoots.Next("sandbox");
        Assert.StartsWith(Path.Combine(artifacts!, "test-host"), root, StringComparison.OrdinalIgnoreCase);
        TestDataRoots.Delete(root);

        // The host reads this file back from its own artifacts/test-results/: the mapped output folder works.
        SandboxLane.Note($"system lane as {Environment.UserName}; artifacts {artifacts}; results {results}");
    }

    [Fact(Explicit = true)]
    [SupportedOSPlatform("windows10.0.17763")]
    public void Real_input_and_the_screen_are_refused_as_SYSTEM()
    {
        SandboxLane.SkipUnlessGuest();

        var input = Assert.Throws<InvalidOperationException>(() => RealInput.Press("Escape"));
        Assert.Contains("not as the guest's user", input.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ScreenCapture.Save(Path.Combine(Path.GetTempPath(), "never.png")));
        Assert.Throws<InvalidOperationException>(() => OtherWindow.Show(new ScreenRect(0, 0, 100, 100)));
    }

    [Fact(Explicit = true)]
    [SupportedOSPlatform("windows")]
    public void The_guests_WebView2_registration_names_a_Runtime_it_has()
    {
        SandboxLane.SkipUnlessGuest();

        // The runner sets it at start: the guest's base image can name a version whose files are not there.
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
        using var state = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
        var version = key?.GetValue("pv") as string;
        var location = key?.GetValue("location") as string;
        var folder = state?.GetValue("EBWebView") as string;
        SandboxLane.Note($"WebView2 registration: {version} at {location}; the loader's folder {folder}");
        Assert.NotNull(version);
        Assert.True(File.Exists(Path.Combine(location!, version, "msedgewebview2.exe")), $"No Runtime files for {version} in {location}.");
        Assert.True(File.Exists(Path.Combine(folder ?? "", "msedgewebview2.exe")), $"The loader's folder {folder} has no Runtime.");
    }

    [Fact(Explicit = true)]
    public async Task A_Production_app_finds_the_guests_WebView2_Runtime()
    {
        SandboxLane.SkipUnlessGuest();

        // As SYSTEM the app gets past the Runtime check and creates its environment (its log names the Runtime), then
        // cannot create a controller; it is ended once its log has said which.
        var root = TestDataRoots.Next("system-runtime");
        var start = new System.Diagnostics.ProcessStartInfo(Projects.WindowHost.ExecutablePath) { UseShellExecute = false };
        start.Environment["LOCALAPPDATA"] = root;
        start.Environment["APPDATA"] = root;
        var log = Path.Combine(root, Projects.WindowHost.AppId, "logs", "app.log");
        using var app = System.Diagnostics.Process.Start(start)!;
        var text = "";
        for (var attempt = 0; attempt < 200 && !text.Contains("WebView2 Runtime 1", StringComparison.Ordinal) && !text.Contains("Start-up problem", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            text = File.Exists(log) ? await ReadSharedAsync(log) : "";
        }

        app.Kill(entireProcessTree: true);
        SandboxLane.Note("Production app as SYSTEM: " + string.Join(" | ", text.Split('\n').Where(line => line.Contains("Runtime", StringComparison.Ordinal))));
        Assert.DoesNotContain("Start-up problem", text, StringComparison.Ordinal);
        Assert.Contains("WebView2 Runtime 1", text, StringComparison.Ordinal);
    }

    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Explicit = true)]
    public void The_guest_has_no_network()
    {
        SandboxLane.SkipUnlessGuest();

        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback && adapter.OperationalStatus == OperationalStatus.Up)
            .Select(adapter => $"{adapter.Name} ({adapter.NetworkInterfaceType})")
            .ToList();
        SandboxLane.Note($"network adapters up: {(up.Count == 0 ? "none" : string.Join(", ", up))}");
        Assert.Empty(up);
        Assert.False(NetworkInterface.GetIsNetworkAvailable());
    }
}

/// <summary>The runner's deadline, checked from the host: this test outlives any deadline, so the runner ends it in the guest.</summary>
[Trait(SandboxLane.Trait, "Deadline")]
public sealed class DeadlineProbeTests
{
    [Fact(Explicit = true)]
    public async Task Runs_until_the_runner_ends_it()
    {
        SandboxLane.SkipUnlessGuest();
        SandboxLane.Note("deadline probe started");
        await Task.Delay(TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);
        Assert.Fail("The runner's deadline should have ended this run.");
    }
}
