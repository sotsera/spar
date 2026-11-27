using System.Text;
using Spar.AppWindows;

namespace Spar.Tests.AppWindows;

/// <summary>
/// <c>windows.json</c> (decision "M3d's decide-first questions", item 5): what each kind keeps, the file written once the
/// changes settle and read back at the next start, and a file Spar did not write treated as empty.
/// </summary>
public sealed class PlacementStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "placement", Guid.NewGuid().ToString("N")[..12]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void The_main_window_and_single_instance_kinds_keep_everything_others_their_size_and_zoom_and_opting_out_keeps_nothing()
    {
        var builder = Kinds();

        Assert.Equal(PlacementRule.Full, PlacementStore.RuleFor(builder.MainWindow));
        Assert.Equal(PlacementRule.Full, PlacementStore.RuleFor(builder.WindowKinds["panel"]));
        Assert.Equal(PlacementRule.SizeAndZoom, PlacementStore.RuleFor(builder.WindowKinds["note"]));
        Assert.Equal(PlacementRule.SizeAndZoom, PlacementStore.RuleFor(builder.WindowKinds["confirm"]));
        Assert.Equal(PlacementRule.None, PlacementStore.RuleFor(builder.WindowKinds["scratch"]));
    }

    [Fact]
    public async Task Changes_are_written_once_they_settle_and_the_next_start_reads_them()
    {
        var builder = Kinds();
        var store = PlacementStore.Open(_root);

        store.Update(builder.MainWindow, new SavedPlacement(100, 80, 1000, 700, false, 1.25));
        store.Update(builder.WindowKinds["note"], new SavedPlacement(300, 200, 600, 400, true, 1));
        store.Update(builder.WindowKinds["scratch"], new SavedPlacement(1, 2, 3, 4, false, 1));
        var early = File.Exists(store.FilePath);
        await WaitForAsync(() => File.Exists(store.FilePath));
        var next = PlacementStore.Open(_root);

        Assert.False(early);
        Assert.Equal(new SavedPlacement(100, 80, 1000, 700, false, 1.25), next.Get(builder.MainWindow));
        Assert.Equal(new SavedPlacement(null, null, 600, 400, false, 1), next.Get(builder.WindowKinds["note"]));
        Assert.Null(next.Get(builder.WindowKinds["scratch"]));
        Assert.DoesNotContain("scratch", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Flush_writes_what_is_pending_and_a_file_Spar_did_not_write_counts_as_empty()
    {
        var builder = Kinds();
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "windows.json"), "{ not json");
        var warnings = new List<string>();

        var store = PlacementStore.Open(_root, warnings.Add);
        var before = store.Get(builder.MainWindow);
        store.Update(builder.MainWindow, new SavedPlacement(null, null, 800, 600, false, 1));
        store.Flush();
        var written = File.ReadAllText(store.FilePath, Encoding.UTF8);

        Assert.Null(before);
        Assert.Contains("is not the file Spar writes", Assert.Single(warnings), StringComparison.Ordinal);
        Assert.Contains("\"width\": 800", written, StringComparison.Ordinal);
        Assert.DoesNotContain("left", written, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"version":1,"windows":{"main":{"width":0,"height":600}}}""")]
    [InlineData("""{"version":1,"windows":{"main":{"width":800}}}""")]
    [InlineData("""{"version":1,"windows":{"main":{"left":5,"width":800,"height":600,"zoom":-1}}}""")]
    public void A_place_that_is_not_sensible_is_left_out_or_corrected(string json)
    {
        var parsed = PlacementStore.Parse(Encoding.UTF8.GetBytes(json)).ToList();

        Assert.True(parsed.Count == 0 || parsed[0].Placement is { Left: null, Top: null, Zoom: 1 });
    }

    private static SparBuilder Kinds()
    {
        var builder = new SparBuilder([]);
        builder.Window("note", w => w.Route("/note"));
        builder.Window("panel", w => w.Route("/panel").SingleInstance());
        builder.Window("confirm", w => w.Route("/confirm").Modal().SingleInstance());
        builder.Window("scratch", w => w.Route("/scratch").RememberPlacement(false));
        return builder;
    }

    private static async Task WaitForAsync(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < limit)
        {
            await Task.Delay(50, Token);
        }
    }
}
