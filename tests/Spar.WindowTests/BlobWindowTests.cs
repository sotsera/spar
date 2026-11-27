using System.Text.Json;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// Binary data on the desktop hosts' own origin (decision "M3b's decide-first questions", item 2): a handle read from
/// the WebView until the page releases it, and resources shown in <c>&lt;img&gt;</c> elements under the default page
/// policy, on WebView2 and WebKitGTK alike.
/// </summary>
[Trait("Surface", "Window")]
public sealed class BlobWindowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_handle_reads_on_the_hosts_origin_until_the_page_releases_it()
    {
        await using var host = await HostPage.OpenAsync();

        using var outcome = await BlobOutcomeAsync(host.Page, "window.hostBlobs.read(1000000, 2)");

        Assert.True(outcome.RootElement.GetProperty("intact").GetBoolean());
        Assert.Equal(404, outcome.RootElement.GetProperty("afterRelease").GetInt32());
        host.AssertClean(allowed404s: 1);
    }

    [Fact]
    public async Task A_page_shows_resources_under_its_default_policy()
    {
        await using var host = await HostPage.OpenAsync();

        using var outcome = await BlobOutcomeAsync(host.Page, "window.hostBlobs.images(100)");

        Assert.Equal(100, outcome.RootElement.GetProperty("loaded").GetInt32());
        host.AssertClean();
    }

    /// <summary>Starts a measurement in the page and waits for the outcome it leaves in <c>window.hostBlobResult</c>.</summary>
    internal static async Task<JsonDocument> BlobOutcomeAsync(SparPage page, string start)
    {
        await page.EvaluateAsync(start);
        var limit = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var text = await page.EvaluateAsync<string>("window.hostBlobResult");
            if (text.Length > 0)
            {
                return JsonDocument.Parse(text);
            }

            Assert.True(DateTime.UtcNow < limit, $"The page did not finish {start} within 60 s.");
            await Task.Delay(20, Token);
        }
    }
}
