using Spar;
using Spar.Testing;
using WireShapes;

namespace WireShapes.Tests;

/// <summary>
/// The typed test clients of binary data (decision "M3b's decide-first questions", item 2), over the app's real dispatch:
/// a handle comes back as the blob it refers to, inside a record too, and a resource as one load of its address runs it.
/// </summary>
public sealed class FilesClientTests : IAsyncLifetime
{
    private SparTestApp _app = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FilesClient Files => new(_app);

    public async ValueTask InitializeAsync() => _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_handle_reads_as_the_bytes_the_command_gave(bool copy)
    {
        var blob = copy ? await Files.CopyAsync(70_000, Token) : await Files.MakeAsync(70_000, Token);

        Assert.Equal((70_000L, "application/octet-stream"), (blob.Length, blob.ContentType));
        Assert.Equal(FilesService.Pattern(70_000), await ReadAsync(blob));
    }

    [Fact]
    public async Task A_blob_inside_a_record_keeps_its_name()
    {
        var export = await Files.ExportAsync("totals", Token);

        Assert.Equal(("totals", "totals.csv", "text/csv"), (export.Title, export.File.Name, export.File.ContentType));
        Assert.Equal("a,b\n1,2\n"u8.ToArray(), await ReadAsync(export.File));
    }

    [Fact]
    public async Task A_resource_runs_for_each_load_and_null_is_no_blob()
    {
        var label = await Files.LabelAsync(3, Token);
        var missing = await Files.PhotoAsync(new PhotoKey(1, -1), Token);

        Assert.Equal("n=3"u8.ToArray(), await ReadAsync(label!));
        Assert.Null(missing);
    }

    private static async Task<byte[]> ReadAsync(SparBlob blob)
    {
        await using var stream = blob.OpenRead();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Token);
        return copy.ToArray();
    }
}
