using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The page's network status on Linux (plan M1c step 7; decision "Network status": the page reads the web's own
/// <c>navigator.onLine</c> and its <c>online</c> and <c>offline</c> events, with no SDK API). The test process runs in
/// a network namespace of its own, inside an unprivileged user namespace, whose only link is the loopback with a default
/// route (<c>eng/linux-ci/network-switch.sh</c>); the test asks the script to remove the route and add it back, which
/// is what GIO's network monitor reads from netlink and WebKit follows. Calls through the bridge keep working while the
/// page is offline, since the bridge never uses the network. No root on the host and no system setting is involved.
/// The switch is the whole namespace's, so every window of the test process would go offline with it: the test runs
/// alone, in a collection of its own.
/// </summary>
[CollectionDefinition(nameof(NetworkSwitchCollection), DisableParallelization = true)]
public sealed class NetworkSwitchCollection;

[Trait("Surface", "Window")]
[Collection(nameof(NetworkSwitchCollection))]
public sealed class NetworkStatusTests
{
    /// <summary>The control FIFO of <c>eng/linux-ci/network-switch.sh</c>, which takes <c>offline</c> and <c>online</c>.</summary>
    private const string SwitchVariable = "SPAR_TEST_NETWORK_SWITCH";

    private static readonly TimeSpan EventLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task The_page_sees_offline_then_online_and_its_calls_work_offline()
    {
        var control = FindSwitch();
        await using var host = await HostPage.OpenAsync();

        Assert.True(await host.Page.EvaluateAsync<bool>(
            """
            () => {
              globalThis.__networkEvents = [];
              for (const type of ["online", "offline"]) {
                addEventListener(type, () => globalThis.__networkEvents.push({ type, at: Date.now(), onLine: navigator.onLine }));
              }
              return navigator.onLine;
            }
            """), "The page started offline, although the namespace has its default route.");

        var offline = await SwitchAsync(control, host, online: false);
        Assert.False(await host.Page.EvaluateAsync<bool>("navigator.onLine"));
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");

        var online = await SwitchAsync(control, host, online: true);
        Assert.True(await host.Page.EvaluateAsync<bool>("navigator.onLine"));
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");

        var events = await host.Page.EvaluateAsync<string>("JSON.stringify(globalThis.__networkEvents.map(e => e.type + ':' + e.onLine))");
        Assert.Equal("""["offline:false","online:true"]""", events);
        Results.Append("network-status.txt", string.Create(CultureInfo.InvariantCulture,
            $"route removal requested to the page's offline event {offline:0} ms, route added to its online event {online:0} ms"));
        host.AssertClean();
    }

    /// <summary>The switch's control FIFO, or a skip with why (a failure on CI, whose Linux job runs under the switch).</summary>
    private static string FindSwitch()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(),
            "Switching the network is built on Linux (a network namespace of the test process's own); on Windows the page offline is checked in Windows Sandbox (OfflineTests).");
        if (Environment.GetEnvironmentVariable(SwitchVariable) is { Length: > 0 } control && File.Exists(control))
        {
            Assert.True(HasDefaultRoute(), "The network switch's namespace has no default route at the start.");
            return control;
        }

        var problem = $"The network switch is not there ({SwitchVariable} names no FIFO): run the tests under eng/linux-ci/network-switch.sh, as the Linux CI job does.";
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(problem + " (CI=true: the Linux CI job runs the window tests under the switch.)");
        }

        Assert.Skip(problem);
        return "";
    }

    /// <summary>
    /// Switches the route, waits until the namespace shows it, then until the page has the matching event, and returns
    /// the time from the request to the page's event in milliseconds (both read from the system clock).
    /// </summary>
    private static async Task<double> SwitchAsync(string control, HostPage host, bool online)
    {
        var type = online ? "online" : "offline";
        var before = await host.Page.EvaluateAsync<int>("globalThis.__networkEvents.length");
        var switched = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Opening a FIFO for writing waits for its reader: a switch that stopped reading fails here instead of hanging.
        await Task.Run(() =>
        {
            using var stream = new FileStream(control, FileMode.Open, FileAccess.Write);
            stream.Write(Encoding.ASCII.GetBytes(type + "\n"));
        }).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Waits.UntilAsync(() => HasDefaultRoute() == online, TimeSpan.FromSeconds(5), $"the default route to be {(online ? "added" : "removed")}");

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var seen = await host.Page.EvaluateAsync<JsonElement>(
                $"globalThis.__networkEvents.slice({before}).find(e => e.type === '{type}') ?? null");
            if (seen.ValueKind == JsonValueKind.Object)
            {
                Assert.Equal(online, seen.GetProperty("onLine").GetBoolean());
                return seen.GetProperty("at").GetDouble() - switched;
            }

            Assert.True(Stopwatch.GetElapsedTime(started) < EventLimit, $"The page had no '{type}' event {EventLimit.TotalSeconds:0} s after the route changed.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Whether this network namespace has an IPv4 default route (a destination of 0 in <c>/proc/net/route</c>).</summary>
    private static bool HasDefaultRoute() =>
        File.ReadLines("/proc/net/route").Skip(1).Any(line => line.Split('\t', StringSplitOptions.TrimEntries) is [_, "00000000", ..]);
}
