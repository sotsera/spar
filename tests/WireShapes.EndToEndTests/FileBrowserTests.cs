using System.Text.Json;

namespace WireShapes.EndToEndTests;

/// <summary>
/// Binary data from the page over the loopback host (decision "M3b's decide-first questions", item 2): handles from
/// results read as often as the page likes until it releases them, a blob inside a record, resources that run their
/// command at each load, and images shown under the default page policy, all with the page's own key.
/// </summary>
[Trait("Surface", "Browser")]
public sealed class FileBrowserTests(SharedBrowser shared)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_handle_reads_until_the_page_releases_it(bool copy)
    {
        await using var wire = await WirePage.OpenAsync(shared);

        using var outcome = JsonDocument.Parse(await wire.CallAsync("([size, copy]) => window.wire.blobRoundTrip(size, copy)", new object[] { 300_000, copy }));
        var root = outcome.RootElement;

        Assert.Equal(300_000, root.GetProperty("size").GetInt32());
        Assert.Equal(copy ? "copied.bin" : "made.bin", root.GetProperty("name").GetString());
        Assert.Equal("/_spar/blob/<id>?k=<key>", root.GetProperty("url").GetString());
        Assert.True(root.GetProperty("intact").GetBoolean());
        Assert.Equal(200, root.GetProperty("again").GetInt32());
        Assert.StartsWith("attachment;", root.GetProperty("disposition").GetString(), StringComparison.Ordinal);
        Assert.Equal(404, root.GetProperty("afterRelease").GetInt32());
        await wire.AssertCleanAsync(expected404s: 1);
    }

    [Fact]
    public async Task A_blob_in_a_record_arrives_with_its_name_and_type()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var outcome = await wire.CallAsync("title => window.wire.exportCsv(title)", "totals");

        Assert.Equal("""{"title":"totals","name":"totals.csv","type":"text/csv","text":"a,b\n1,2\n"}""", outcome);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task Each_load_of_a_resource_runs_its_command()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var outcome = await wire.CallAsync("n => window.wire.resources(n)", 42);

        Assert.Equal("""{"label":"n=42","labelAgain":"n=42","type":"text/plain; charset=utf-8","missing":404}""", outcome);
        await wire.AssertCleanAsync(expected404s: 1);
    }

    [Fact]
    public async Task A_page_shows_any_number_of_resources_under_its_default_policy()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var outcome = await wire.CallAsync("count => window.wire.showPhotos(count)", 200);

        Assert.Equal("""{"loaded":200}""", outcome);
        await wire.AssertCleanAsync();
    }
}
