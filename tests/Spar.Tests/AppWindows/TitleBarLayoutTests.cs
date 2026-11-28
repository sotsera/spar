using System.Text;
using Spar.AppWindows;
using Spar.Files;
using Spar.Runtime;

namespace Spar.Tests.AppWindows;

/// <summary>
/// The page-drawn title bar's layout (<c>docs/app-model.md</c> 6.9), as the host takes it from the SDK and hit-tests
/// from it: the buttons first, then the drag areas without the controls in them; the limits on what a page may post;
/// <c>spar.titleBar</c> refused for a window with the system's title bar; and the window's state event the page starts from.
/// </summary>
public sealed class TitleBarLayoutTests
{
    private const string Layout = """
        {"bars":[[0,0,800,32]],"holes":[[100,4,160,24]],"minimize":[662,0,46,32],"maximize":[708,0,46,32],"close":[754,0,46,32]}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_point_is_a_button_then_the_bar_without_its_controls_then_the_page()
    {
        Assert.True(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes(Layout), out var layout, out _));

        Assert.Equal(TitleBarHit.Drag, layout.HitTest(10, 10));
        Assert.Equal(TitleBarHit.Client, layout.HitTest(150, 10));
        Assert.Equal(TitleBarHit.Minimize, layout.HitTest(670, 5));
        Assert.Equal(TitleBarHit.Maximize, layout.HitTest(708, 31.9));
        Assert.Equal(TitleBarHit.Close, layout.HitTest(799.5, 0));
        Assert.Equal(TitleBarHit.Client, layout.HitTest(400, 32));
        Assert.Equal(TitleBarHit.Client, TitleBarLayout.Empty.HitTest(10, 10));
        Assert.Equal(3, layout.Buttons.Count());
    }

    [Theory]
    [InlineData("""{"bars":[[0,0,800]]}""")]
    [InlineData("""{"bars":[[0,0,800,32,1]]}""")]
    [InlineData("""{"bars":[[0,0,-1,32]]}""")]
    [InlineData("""{"bars":[[0,0,1e6,32]]}""")]
    [InlineData("""{"close":[0,0,"a",32]}""")]
    [InlineData("""{"bars":{}}""")]
    [InlineData("""[]""")]
    [InlineData("""{"bars":[]} {}""")]
    public void A_layout_that_is_not_rectangles_of_finite_numbers_is_refused(string json)
    {
        Assert.False(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes(json), out var layout, out var problem));
        Assert.Same(TitleBarLayout.Empty, layout);
        Assert.Contains("titleBar", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layout_holds_at_most_16_bars_and_256_controls_and_32_KB()
    {
        var bars = string.Join(",", Enumerable.Repeat("[0,0,1,1]", TitleBarLayout.MaxBars + 1));
        var holes = string.Join(",", Enumerable.Repeat("[0,0,1,1]", TitleBarLayout.MaxHoles));

        Assert.False(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes($$"""{"bars":[{{bars}}]}"""), out _, out _));
        Assert.True(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes($$"""{"holes":[{{holes}}],"other":1}"""), out var full, out _));
        Assert.Equal(TitleBarLayout.MaxHoles, full.Holes.Count);
        Assert.False(TitleBarLayout.TryRead(new byte[TitleBarLayout.MaxBytes + 1], out _, out var problem));
        Assert.Contains("32 KB", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_layout_again_changes_nothing_and_a_new_one_tells_the_host()
    {
        var changes = 0;
        var state = new TitleBarState { LayoutChanged = () => changes++ };
        Assert.True(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes(Layout), out var first, out _));
        Assert.True(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes(Layout), out var again, out _));
        Assert.True(TitleBarLayout.TryRead(Encoding.UTF8.GetBytes("""{"bars":[[0,0,900,32]]}"""), out var wider, out _));

        Assert.True(state.Update(first));
        Assert.False(state.Update(again));
        Assert.True(state.Update(wider));
        Assert.Equal(2, changes);
        Assert.Same(wider, state.Layout);
    }

    [Fact]
    public async Task The_page_posts_its_layout_to_a_custom_title_bar_and_is_refused_for_the_systems()
    {
        var custom = new HostWindow(TimeProvider.System, dialogs: null) { TitleBar = new TitleBarState() };
        var system = new HostWindow(TimeProvider.System, dialogs: null);

        var taken = await WindowCommands.InvokeAsync(custom, WindowCommands.TitleBarLayoutCommand, Encoding.UTF8.GetBytes(Layout), Token);
        var refused = await WindowCommands.InvokeAsync(system, WindowCommands.TitleBarLayoutCommand, Encoding.UTF8.GetBytes(Layout), Token);
        var malformed = await WindowCommands.InvokeAsync(custom, WindowCommands.TitleBarLayoutCommand, "{\"bars\":1}"u8.ToArray(), Token);

        Assert.Equal(SparOutcomeKind.Value, taken.Kind);
        Assert.Equal(TitleBarHit.Close, custom.TitleBar!.Layout.HitTest(760, 10));
        Assert.Equal("invalid-request", refused.Failure?.Code);
        Assert.Contains("TitleBar(TitleBar.Custom)", refused.Failure?.Detail, StringComparison.Ordinal);
        Assert.Equal("invalid-request", malformed.Failure?.Code);
        Assert.True(WindowCommands.Handles(WindowCommands.TitleBarLayoutCommand));
    }

    [Fact]
    public void The_events_say_the_windows_state_and_the_buttons_look()
    {
        Assert.Equal("""{"maximized":true}""", Encoding.UTF8.GetString(TitleBarState.StatePayload(true).Span));
        Assert.Equal("""{"hover":"close","pressed":null}""", Encoding.UTF8.GetString(TitleBarState.CaptionPayload(TitleBarHit.Close, TitleBarHit.Client).Span));
    }

    [Fact]
    public void The_title_bar_option_takes_the_two_kinds_only()
    {
        var builder = new SparBuilder([]);
        builder.Window(w => w.TitleBar(TitleBar.Custom));

        Assert.Equal(TitleBar.Custom, builder.MainWindow.TitleBarKind);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.MainWindow.TitleBar((TitleBar)7));
    }
}
