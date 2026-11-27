using System.Text;
using System.Text.Json;
using Spar.Bridge;
using Spar.Hosting;

namespace Spar.Tests.Blobs;

/// <summary>
/// Binary data from the backend (decision "M3b's decide-first questions", item 2): a result's blob becomes a handle of
/// its document, read on the origin as often as the page likes, until the page releases it or the document goes; the
/// document's limits fail the call that would pass them and release nothing; a resource runs its command per load.
/// </summary>
public sealed class BlobTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_results_blob_is_a_handle_the_origin_serves_until_the_page_releases_it()
    {
        await using var bridge = await TestBridge.OpenAsync();

        var id = await MakeAsync(bridge, 1, 300_000);
        var first = await GetAsync(bridge, $"/_spar/blob/{id}");
        var again = await GetAsync(bridge, $"/_spar/blob/{id}");
        bridge.Session.Receive(TestBridge.Call(2, "spar", "releaseBlob", $"\"{id}\""));
        var released = await bridge.Sent.NextAsync();
        var gone = await GetAsync(bridge, $"/_spar/blob/{id}");

        Assert.Equal(200, first.Status);
        Assert.Equal(TestBlobs.Pattern(300_000), first.Body.ToArray());
        Assert.Equal("attachment; filename=\"made.bin\"; filename*=UTF-8''made.bin", first.Disposition);
        Assert.Equal("sandbox; default-src 'none'", first.Csp);
        Assert.Equal(200, again.Status);
        Assert.True(released.Ok);
        Assert.Equal(404, gone.Status);
        Assert.Equal(0, bridge.Session.Blobs.Count);
    }

    [Fact]
    public async Task The_documents_handles_go_with_it()
    {
        await using var bridge = await TestBridge.OpenAsync();
        var id = await MakeAsync(bridge, 1, 10);

        bridge.Session.Close();

        Assert.Equal(404, (await GetAsync(bridge, $"/_spar/blob/{id}")).Status);
        Assert.Equal(0, bridge.Backend.Dispatcher.Blobs.Count);
    }

    [Fact]
    public async Task A_result_past_the_handle_limit_fails_and_nothing_is_released()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { MaxBlobs = 2 });
        await MakeAsync(bridge, 1, 10);
        await MakeAsync(bridge, 2, 10);

        bridge.Session.Receive(TestBridge.Call(3, "files", "make", "10"));
        var failure = await bridge.Sent.NextAsync();

        Assert.Equal(("response", "too-large", "completed"), (failure.Kind, failure.Code, failure.Execution));
        Assert.Equal(2, bridge.Session.Blobs.Count);
    }

    [Fact]
    public async Task A_result_past_the_byte_limit_fails_and_the_others_stay()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { MaxBlobBytes = 1000 });
        await MakeAsync(bridge, 1, 600);

        bridge.Session.Receive(TestBridge.Call(2, "files", "make", "600"));
        var failure = await bridge.Sent.NextAsync();
        await MakeAsync(bridge, 3, 400);

        Assert.Equal("too-large", failure.Code);
        Assert.Equal((2, 1000L), (bridge.Session.Blobs.Count, bridge.Session.Blobs.Bytes));
    }

    [Fact]
    public async Task A_resource_runs_its_command_for_each_load_and_keeps_nothing()
    {
        await using var backend = TestBackend.Create();

        var photo = await BlobRoutes.AnswerAsync(backend.Dispatcher, "GET", "/_spar/resource/files/photo", "r=42", null, Token);
        var head = await BlobRoutes.AnswerAsync(backend.Dispatcher, "HEAD", "/_spar/resource/files/photo", "r=42", null, Token);
        var missing = await BlobRoutes.AnswerAsync(backend.Dispatcher, "GET", "/_spar/resource/files/photo", "r=-1", null, Token);

        Assert.Equal((200, "image/png", null), (photo.Status, photo.ContentType, photo.Disposition));
        Assert.EndsWith("PNG#42", Encoding.ASCII.GetString(photo.Body.Span), StringComparison.Ordinal);
        Assert.Equal((200, 0), (head.Status, head.Body.Length));
        Assert.Equal(404, missing.Status);
        Assert.Equal(0, backend.Dispatcher.Blobs.Count);
    }

    [Theory]
    [InlineData("POST", "/_spar/resource/files/photo", "r=1", 405)]
    [InlineData("GET", "/_spar/resource/files/make", "r=1", 400)]
    [InlineData("GET", "/_spar/resource/files/nothing", "", 404)]
    [InlineData("GET", "/_spar/resource/files", "", 404)]
    [InlineData("GET", "/_spar/resource/files/photo", "r=%22text%22", 400)]
    [InlineData("GET", "/_spar/blob/0123456789abcdef0123456789abcdef", "", 404)]
    public async Task What_is_not_a_load_is_refused(string method, string path, string query, int status)
    {
        await using var backend = TestBackend.Create();

        var answer = await BlobRoutes.AnswerAsync(backend.Dispatcher, method, path, query, null, Token);

        Assert.Equal(status, answer.Status);
    }

    [Fact]
    public async Task A_resource_is_never_called_through_the_bridge()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(TestBridge.Call(1, "files", "photo", "1"));
        var refused = await bridge.Sent.NextAsync();

        Assert.Equal(("rejected", "invalid-request"), (refused.Kind, refused.Code));
    }

    [Fact]
    public async Task On_loopback_only_the_documents_own_key_reads_its_handles()
    {
        await using var bridge = await TestBridge.OpenAsync();
        var id = await MakeAsync(bridge, 1, 10);
        var other = bridge.Backend.Dispatcher.Blobs.Open(100, 100);
        BlobOwner? Keys(string key) => key == "mine" ? bridge.Session.Blobs : key == "other" ? other : null;

        var mine = await BlobRoutes.AnswerAsync(bridge.Backend.Dispatcher, "GET", $"/_spar/blob/{id}", "k=mine", Keys, Token);
        var theirs = await BlobRoutes.AnswerAsync(bridge.Backend.Dispatcher, "GET", $"/_spar/blob/{id}", "k=other", Keys, Token);
        var none = await BlobRoutes.AnswerAsync(bridge.Backend.Dispatcher, "GET", $"/_spar/blob/{id}", "", Keys, Token);

        Assert.Equal((200, 404, 401), (mine.Status, theirs.Status, none.Status));
    }

    [Fact]
    public async Task A_stream_copy_holds_no_memory_and_its_file_goes_with_the_last_handle()
    {
        var folder = Path.Combine(TestFolders.Artifacts(), "blob-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var blob = await SparBlob.FromStreamAsync(new MemoryStream(TestBlobs.Pattern(5000)), folder, "image/png", "a.png", Token);
            var store = new BlobStore();
            var owner = store.Open(maxBytes: 100, maxHandles: 10);

            var id = owner.Add(blob);
            var file = Assert.Single(Directory.GetFiles(folder));
            owner.Release(id);

            Assert.Equal(0, owner.Bytes);
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("video/mp4", true)]
    [InlineData("text/plain; charset=utf-8", true)]
    [InlineData("application/pdf", true)]
    [InlineData("text/html", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("application/javascript", false)]
    [InlineData("application/octet-stream", false)]
    public void Only_media_a_page_shows_is_served_inline(string contentType, bool inline)
    {
        Assert.Equal(inline, BlobContentTypes.IsInline(contentType));
    }

    [Theory]
    [InlineData("text/html\r\nSet-Cookie: a=b")]
    [InlineData("png")]
    [InlineData("image/")]
    [InlineData("image/png/x")]
    [InlineData("image/pn g")]
    public void A_type_that_is_not_a_media_type_is_refused(string contentType)
    {
        Assert.Throws<ArgumentException>(() => SparBlob.FromBytes([1], contentType));
    }

    /// <summary>Calls <c>files.make</c> and returns the handle's id from its result.</summary>
    private static async Task<string> MakeAsync(TestBridge bridge, long callId, int size)
    {
        bridge.Session.Receive(TestBridge.Call(callId, "files", "make", size.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var result = await bridge.Sent.NextAsync();
        Assert.True(result.Ok, $"{result.Kind}/{result.Code}");
        using var handle = JsonDocument.Parse(result.Payload);
        Assert.Equal(size, handle.RootElement.GetProperty("size").GetInt64());
        return handle.RootElement.GetProperty("id").GetString()!;
    }

    private static Task<PageResponse> GetAsync(TestBridge bridge, string path) =>
        BlobRoutes.AnswerAsync(bridge.Backend.Dispatcher, "GET", path, "", null, Token);
}
