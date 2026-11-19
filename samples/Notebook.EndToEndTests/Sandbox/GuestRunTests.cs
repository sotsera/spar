using System.Runtime.Versioning;
using Spar;
using Spar.Testing;

namespace Notebook.EndToEndTests;

/// <summary>The Notebook's payload in the runner's SYSTEM lane: it runs in the guest and writes where the runner said.</summary>
[Trait(SandboxLane.Trait, "System")]
public sealed class SystemLaneTests
{
    [Fact(Explicit = true)]
    public void The_payload_runs_in_the_guest_and_writes_its_results_where_the_runner_said()
    {
        SandboxLane.SkipUnlessGuest();

        Assert.True(File.Exists(Projects.Notebook.ExecutablePath), "The Production executable came with the payload.");
        Assert.StartsWith(@"C:\spar\out\", TestArtifacts.Results("Notebook.EndToEndTests"), StringComparison.OrdinalIgnoreCase);
        SandboxLane.Note($"system lane as {Environment.UserName}; executable {new FileInfo(Projects.Notebook.ExecutablePath).Length} B");
    }
}

/// <summary>
/// The Production executable's first run on a clean machine (plan M1b step 8: "the offline Production run"): a sandbox
/// of its own, never used before, with no network. It starts, shows its page, creates its folders under the data root,
/// and closes cleanly.
/// </summary>
[Trait(SandboxLane.Trait, "CleanMachine")]
[SupportedOSPlatform("windows10.0.17763")]
public sealed class CleanMachineTests
{
    [Fact(Explicit = true)]
    public async Task The_first_run_on_a_clean_offline_machine_starts_shows_its_page_and_closes()
    {
        SandboxLane.SkipUnlessUserSession();
        var token = TestContext.Current.CancellationToken;
        Assert.False(System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(), "The guest is offline.");

        await using var app = await SparBlackBox.LaunchAsync(Projects.Notebook, cancellationToken: token);
        Assert.Equal("Notebook", await app.Window.TitleAsync());
        await app.Window.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" }).WaitForAsync();
        await SandboxLane.EvidenceAsync(app, "clean-machine-first-run");

        Assert.Equal(0, await app.CloseAsync(token));
        Assert.True(await app.WaitForProcessTreeExitAsync());
        Assert.True(File.Exists(app.LogFile), "The log was written.");
        Assert.True(Directory.Exists(Path.Combine(app.AppFolder, "WebView2", "EBWebView")), "The browser profile was created.");
        SandboxLane.Note($"clean machine: data folder {(Directory.Exists(app.DataFolder) ? "created" : "not created")}; ready after {app.StartedAfter.TotalMilliseconds:0} ms; folders under {app.AppFolder}");
    }
}
