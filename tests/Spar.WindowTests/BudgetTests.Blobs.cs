using WireShapes.Tests;

namespace Spar.WindowTests;

/// <summary>
/// The binary data budget on the desktop bridges (decision "M3b's decide-first questions", item 5): a 12 MB handle read
/// by the page from the host's origin, timed by the page from the address to the last byte, median of five after a first
/// run, at most 150 ms in Release on both hosts.
/// </summary>
public sealed partial class BudgetTests
{
    private const int BlobBytes = 12 * 1024 * 1024;
    private const int BlobRuns = 5;

    [Fact]
    public async Task A_twelve_megabyte_blob_crosses_the_desktop_bridge()
    {
        await using var host = await HostPage.OpenAsync();

        using var outcome = await BlobWindowTests.BlobOutcomeAsync(host.Page, $"window.hostBlobs.read({BlobBytes}, {BlobRuns + 1})");
        Assert.True(outcome.RootElement.GetProperty("intact").GetBoolean());
        var durations = outcome.RootElement.GetProperty("times").EnumerateArray().Skip(1).Select(time => time.GetDouble()).ToList();

        Budgets.Record("Desktop bridge: a 12 MB blob read by the page, timed by the page, first run left out", Budgets.Summary(durations));
        host.AssertClean(allowed404s: 1);
    }
}
