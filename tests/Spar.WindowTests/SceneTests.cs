using System.Buffers.Binary;
using System.Text.Json;
using Spar.Testing;
using WindowHost;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Scenes (decision "M2c's decide-first questions", items 4 and 5): two captures of one scene on the same machine are
/// the same bytes, the page is the scene's size at a scale of 1 whatever the monitor's, and the backend's and the page's
/// clocks start at the scene's instant and run from there. The scene below is also a generated window test.
/// </summary>
[Trait("Surface", "Window")]
public sealed class SceneTests
{
    private static readonly DateTimeOffset Instant = new(2031, 5, 17, 14, 45, 0, TimeSpan.Zero);

    /// <summary>The test app's page, ready.</summary>
    [SparScene("window-host")]
    internal static async Task WindowHostScene(SparScene scene)
    {
        await WindowSupport.SkipUnlessSupportedAsync();
        var app = await scene.StartAsync<Program>();
        await Expect(app.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Ready");
    }

    /// <summary>
    /// The test app's page with a change still to come from a timeout, a second after the scene ends: without anything
    /// else going on, the capture's idle wait alone would be over before it.
    /// </summary>
    [SparScene("pending-timeout")]
    internal static async Task PendingTimeoutScene(SparScene scene)
    {
        await WindowSupport.SkipUnlessSupportedAsync();
        var app = await scene.StartAsync<Program>();
        await Expect(app.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Ready");
        await app.Page.EvaluateAsync("""() => { setTimeout(() => { const button = document.createElement("button"); button.textContent = "Later"; document.getElementById("late").replaceChildren(button); }, 1000); }""");
    }

    [Fact]
    public async Task A_capture_waits_for_the_pages_pending_timeouts()
    {
        var capture = await SparScenes.CaptureAsync("pending-timeout", TestContext.Current.CancellationToken);

        Assert.Contains("\"Later\"", capture.Tree, StringComparison.Ordinal);
        Assert.Empty(capture.Problems);
    }

    [Fact]
    public async Task Two_captures_of_a_scene_are_the_same_bytes_at_its_size()
    {
        var token = TestContext.Current.CancellationToken;

        var first = await SparScenes.CaptureAsync("window-host", token);
        var second = await SparScenes.CaptureAsync("window-host", token);

        Assert.Equal((SparScene.DefaultSize.Width, SparScene.DefaultSize.Height), Size(first.Png));
        if (!first.Png.AsSpan().SequenceEqual(second.Png))
        {
            var folder = Results.Folder(Path.Combine("scenes", $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "first.png"), first.Png, token);
            await File.WriteAllBytesAsync(Path.Combine(folder, "second.png"), second.Png, token);
            await File.WriteAllTextAsync(Path.Combine(folder, "first-tree.json"), first.Tree, token);
            await File.WriteAllTextAsync(Path.Combine(folder, "second-tree.json"), second.Tree, token);
            Assert.Fail($"The two captures' PNGs differ; both are in {folder}.");
        }
        Assert.Equal(first.Tree, second.Tree);
        Assert.Empty(first.Problems);
        using var calls = JsonDocument.Parse(first.Calls);
        Assert.Equal(JsonValueKind.Array, calls.RootElement.ValueKind);
    }

    [Fact]
    public async Task The_backend_and_the_page_read_the_scenes_clock()
    {
        await WindowSupport.SkipUnlessSupportedAsync();
        await using var scene = new SparScene("clock", TestContext.Current.CancellationToken) { Time = Instant };

        var app = await scene.StartAsync<Program>();
        var page = await app.Page.EvaluateAsync<double>("Date.now()");
        var constructed = await app.Page.EvaluateAsync<double>("new Date().getTime()");
        var parsed = await app.Page.EvaluateAsync<double>("new Date('2020-01-01T00:00:00Z').getTime()");
        var isDate = await app.Page.EvaluateAsync<bool>("new Date() instanceof Date");
        var backend = app.Time.GetUtcNow();

        var limit = TimeSpan.FromMinutes(1);
        Assert.InRange(DateTimeOffset.FromUnixTimeMilliseconds((long)page), Instant, Instant + limit);
        Assert.InRange(DateTimeOffset.FromUnixTimeMilliseconds((long)constructed), Instant, Instant + limit);
        Assert.InRange(backend, Instant, Instant + limit);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), (long)parsed);
        Assert.True(isDate);
    }

    private static (int Width, int Height) Size(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
}
