using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar.Tests;

/// <summary>
/// The app's page hang hook and the Production stall probe's option (decision "Hangs: the page's hook, the Production
/// stall probe and "Exit now""), as every host runs them.
/// </summary>
public sealed class PageHangHookTests
{
    [Fact]
    public async Task The_hook_sees_every_change_in_order_with_the_apps_services()
    {
        var seen = new List<(PageHang, bool)>();
        var log = new CapturedLog();
        var hook = new PageHangHook((hang, services) => seen.Add((hang, services.GetService<Marker>() is not null)), log);
        await using var services = new ServiceCollection().AddSingleton<Marker>().BuildServiceProvider();

        hook.Report(new PageHang("main", PageHangState.Unresponsive, TimeSpan.FromSeconds(16)));
        hook.Start(services);
        hook.Report(new PageHang("main", PageHangState.Recovered, TimeSpan.FromSeconds(21)));
        await hook.StopAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                (new PageHang("main", PageHangState.Unresponsive, TimeSpan.FromSeconds(16)), true),
                (new PageHang("main", PageHangState.Recovered, TimeSpan.FromSeconds(21)), true),
            ],
            seen);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task A_hook_that_throws_is_logged_and_still_gets_the_next_change()
    {
        var seen = new List<PageHangState>();
        var log = new CapturedLog();
        var hook = new PageHangHook(
            (hang, _) =>
            {
                seen.Add(hang.State);
                if (hang.State == PageHangState.Unresponsive)
                {
                    throw new InvalidOperationException("telemetry is down");
                }
            },
            log);
        await using var services = new ServiceCollection().BuildServiceProvider();
        hook.Start(services);

        hook.Report(new PageHang("main", PageHangState.Unresponsive, TimeSpan.FromSeconds(17)));
        hook.Report(new PageHang("main", PageHangState.Reloaded, TimeSpan.FromSeconds(30)));
        await hook.StopAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal([PageHangState.Unresponsive, PageHangState.Reloaded], seen);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task Without_a_hook_changes_go_nowhere()
    {
        var hook = new PageHangHook(null, new CapturedLog());
        await using var services = new ServiceCollection().BuildServiceProvider();
        hook.Start(services);

        hook.Report(new PageHang("main", PageHangState.ClosedAnyway, TimeSpan.FromSeconds(40)));

        await hook.StopAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_build_without_the_development_switch_has_a_stall_probe_only_when_the_app_keeps_it()
    {
        var builder = new SparBuilder([]);
        Assert.Null(builder.DiagnosticsOptions.CreateStallProbe(new CapturedLog()));

        builder.Diagnostics(d => d.KeepStallProbe());

        Assert.IsType<UiStallProbe>(builder.DiagnosticsOptions.CreateStallProbe(new CapturedLog()));
    }

    private sealed class Marker;
}
