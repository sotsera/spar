using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Testing;
using static Spar.Testing.SparAssertions;
using static Spar.WindowTests.TitleBarTests;

namespace Spar.WindowTests;

/// <summary>
/// The window test's display has no window manager, so GDK moves and resizes the window itself, with a pointer grab of
/// its own on the display every window test shares: while it holds it, other tests' clicks went to the grabbing window
/// (a full run lost three of them). So the real drags on a page-drawn title bar run alone, in a collection of their own.
/// </summary>
[CollectionDefinition(nameof(TitleBarDragCollection), DisableParallelization = true)]
public sealed class TitleBarDragCollection;

/// <summary>
/// On Linux, real input (XTEST) on WindowHost's kind <c>titled</c>: a drag on the bar's empty part moves the window, one
/// on its search box does not, the view's edge resizes it (no compositor, so no frame), and its web process ends once it
/// closes. On Windows the window test's input is the page's own; the Sandbox's user-session lane drags the real frame.
/// </summary>
[Trait("Surface", "Window")]
[Collection(nameof(TitleBarDragCollection))]
public sealed class TitleBarDragTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_drag_on_the_bars_empty_part_moves_the_window_and_one_on_its_edge_resizes_it_on_Linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("The window test's input on Windows is the page's own, which never reaches the frame: the Sandbox's user-session lane drags the real one.");
        }

        await using var host = await HostPage.OpenAsync();
        var titled = await OpenTitledAsync(host);
        var space = (await CentresAsync(titled.Page, ".titlebar-space"))[0];
        var search = (await CentresAsync(titled.Page, "#titlebar-search"))[0];
        _ = await HitWhenLaidOutAsync(titled.Window, space);
        var before = await titled.Window.NativeStateAsync();

        await titled.Page.Mouse.DownAsync(search.X, search.Y);
        await titled.Page.Mouse.MoveAsync(search.X + 30, search.Y + 20);
        await titled.Page.Mouse.UpAsync(search.X + 30, search.Y + 20);
        var unmoved = await titled.Window.NativeStateAsync();
        await titled.Page.Mouse.DownAsync(space.X, space.Y);
        await titled.Page.Mouse.MoveAsync(space.X + 10, space.Y);
        await titled.Page.Mouse.MoveAsync(space.X + 60, space.Y + 40);
        // The window followed the pointer: relative to it, the pointer is back at the press.
        await titled.Page.Mouse.UpAsync(space.X, space.Y);
        var moved = await StateAsync(titled.Window, state => state.Left != before.Left);

        Assert.NotNull(before.Left);
        Assert.Equal((before.Left, before.Top), (unmoved.Left, unmoved.Top));
        Assert.InRange(moved.Left!.Value - before.Left!.Value, 55, 65);
        Assert.InRange(moved.Top!.Value - before.Top!.Value, 35, 45);

        // The page still takes input after the window manager's move (pitfalls, the reset controllers).
        await titled.Page.GetByLabel("Search titles").FillAsync("moved");
        await Expect(titled.Page.GetByLabel("Search titles")).ToHaveValueAsync("moved");

        // The view's right edge resizes the window, which has no frame on a display without a compositor.
        var right = moved.Width - 2;
        await titled.Page.Mouse.DownAsync(right, 200);
        await titled.Page.Mouse.MoveAsync(right + 20, 200);
        await titled.Page.Mouse.MoveAsync(right + 40, 200);
        await titled.Page.Mouse.UpAsync(right + 40, 200);
        var wider = await StateAsync(titled.Window, state => state.Width != moved.Width);
        Assert.InRange(wider.Width - moved.Width, 35, 45);

        // Once the window closes, its web process ends. (The pitfall the controllers' reset fixes, a press GTK 4 kept after a
        // window manager's move took its release, needs a window manager's grab; GDK's own move here does not reproduce it.)
        var renderers = await titled.Window.RendererProcessesAsync();
        _ = await host.App.Services.GetRequiredService<IWindows>().All.Single(window => window.Kind == "titled").CloseAsync();
        await titled.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (renderers.Any(id => Directory.Exists($"/proc/{id}")) && DateTime.UtcNow < limit)
        {
            await Task.Delay(50, Token);
        }

        Assert.NotEmpty(renderers);
        Assert.DoesNotContain(renderers, id => Directory.Exists($"/proc/{id}"));
    }
}
