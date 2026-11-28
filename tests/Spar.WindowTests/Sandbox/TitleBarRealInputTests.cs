using System.Runtime.Versioning;
using Spar.Testing;
using static Spar.WindowTests.FocusAndDialogTests;

namespace Spar.WindowTests;

/// <summary>
/// The page-drawn title bar with real input, on WindowHost's Production executable in the guest's user session
/// (decision "M3d's decide-first questions", item 9; plan 3, "Title bar drawn by the page"): <c>--custom-titlebar</c>
/// gives its main window <c>TitleBar(TitleBar.Custom)</c>. UI Automation shows no native title bar and one set of
/// caption buttons, the page's; a drag on the bar's empty part moves the window and a double click maximizes and
/// restores it; the page's Maximize button maximizes and restores through the host's caption window, and hovering it
/// opens Snap Layouts (a screenshot is the evidence); the top edge resizes the window; a right click on the bar opens
/// the system menu; Close ends the app. Everything happens on the guest's own screen.
/// </summary>
[Trait(SandboxLane.Trait, "UserSession")]
[SupportedOSPlatform("windows10.0.17763")]
public sealed class TitleBarRealInputTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    public async Task The_page_drawn_title_bar_moves_maximizes_snaps_resizes_and_closes_the_window()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, options => options.Arguments.Add("--custom-titlebar"), Token);
        await app.Window.GetByText("Ready", new() { Exact = true }).WaitForAsync();
        var maximize = app.Window.GetByRole(SparRole.Button, new() { Name = "Maximize", Exact = true });
        await maximize.WaitForAsync();
        var start = await app.Window.PartsAsync();
        await SandboxLane.EvidenceAsync(app, "titlebar-start");

        // UI Automation: no native title bar, and the page's caption buttons are the only ones.
        Assert.Null(start.TitleBar);
        Assert.Equal(1, await maximize.CountAsync());

        // A drag on the empty part of the bar, right of the search box, moves the window.
        var search = await app.Window.GetByRole(SparRole.Textbox, new() { Name = "Search titles", Exact = true }).BoundsAsync();
        var spaceX = search.Right + 80;
        var spaceY = search.Center.Y;
        RealInput.Click(spaceX, spaceY);
        RealInput.Drag(spaceX, spaceY, spaceX + 120, spaceY + 60);
        await UntilAsync(async () => (await app.Window.PartsAsync()).Bounds.Left == start.Bounds.Left + 120, "the drag moves the window");
        var moved = await app.Window.PartsAsync();
        Assert.Equal((start.Bounds.Left + 120, start.Bounds.Top + 60), (moved.Bounds.Left, moved.Bounds.Top));

        // A double click there maximizes the window, and another restores it.
        RealInput.DoubleClick(spaceX + 120, spaceY + 60);
        await UntilAsync(async () => (await app.Window.PartsAsync()).Bounds.Left < moved.Bounds.Left - 100, "the double click maximizes the window");
        await SandboxLane.EvidenceAsync(app, "titlebar-maximized");
        var maximizedSearch = await app.Window.GetByRole(SparRole.Textbox, new() { Name = "Search titles", Exact = true }).BoundsAsync();
        RealInput.DoubleClick(maximizedSearch.Right + 80, maximizedSearch.Center.Y);
        await UntilAsync(async () => (await app.Window.PartsAsync()).Bounds == moved.Bounds, "the second double click restores it");

        // The page's Maximize button, through the host's caption window: maximize, then restore by the same button.
        var button = await maximize.BoundsAsync();
        RealInput.Move(button.Center.X, button.Center.Y);
        await Task.Delay(1_500, Token);
        await SandboxLane.EvidenceAsync(app, "titlebar-snap-layouts");
        RealInput.Click(button.Center.X, button.Center.Y);
        await UntilAsync(async () => (await app.Window.PartsAsync()).Bounds.Left < moved.Bounds.Left - 100, "Maximize maximizes the window");
        var restore = await app.Window.GetByRole(SparRole.Button, new() { Name = "Restore", Exact = true }).BoundsAsync();
        RealInput.Click(restore.Center.X, restore.Center.Y);
        await UntilAsync(async () => (await app.Window.PartsAsync()).Bounds == moved.Bounds, "Restore restores it");

        // The top edge resizes the window.
        RealInput.Move(spaceX + 120, moved.Bounds.Top + 3);
        RealInput.Drag(spaceX + 120, moved.Bounds.Top + 3, spaceX + 120, moved.Bounds.Top - 37);
        await UntilAsync(async () => (await app.Window.PartsAsync()).Bounds.Top == moved.Bounds.Top - 40, "the top edge resizes the window");
        var resized = await app.Window.PartsAsync();
        Assert.Equal(moved.Bounds.Bottom, resized.Bounds.Bottom);

        // A right click on the bar opens the window's system menu.
        var menuSearch = await app.Window.GetByRole(SparRole.Textbox, new() { Name = "Search titles", Exact = true }).BoundsAsync();
        RealInput.Click(menuSearch.Right + 80, menuSearch.Center.Y, RealMouseButton.Right);
        await UntilAsync(
            async () => (await app.Window.TopLevelWindowsAsync()).Any(window => window.MenuItems.Contains("Move")),
            "the system menu opens");
        await SandboxLane.EvidenceAsync(app, "titlebar-system-menu");
        await app.Window.PressAsync("Escape");

        // The page's Close button ends the app as its close request does.
        var close = await app.Window.GetByRole(SparRole.Button, new() { Name = "Close", Exact = true }).BoundsAsync();
        RealInput.Click(close.Center.X, close.Center.Y);
        Assert.Equal(0, await app.WaitForExitAsync(Token));
    }
}
