using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The page's zoom in real windows (<c>docs/app-model.md</c> 6.8; decision "The window design, from the windows spike",
/// item 3): <c>zoom.set</c> within the window's limits, the page hearing each change, a kind's window opening at the
/// zoom its last one had, and on Linux the host's own Ctrl+plus, minus and 0. A window test sets the zoom through the
/// host: a synthetic Ctrl+wheel does not zoom WebView2, so the browser's own keys and wheel are not checked on Windows.
/// WindowHost's notes zoom between 0.5 and 2.
/// </summary>
[Trait("Surface", "Window")]
public sealed class ZoomTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_page_sets_its_zoom_within_its_windows_limits_and_a_new_window_opens_at_its_kinds_last_zoom()
    {
        await using var host = await HostPage.OpenAsync();
        var windows = host.App.Services.GetRequiredService<IWindows>();

        await host.ClickAsync("Zoom to 150 %");
        await Expect(host.Page.GetByText("Zoom:")).ToHaveTextAsync("Zoom: 150 %");
        var main = await StateAsync(host.App.Window, state => state.Zoom == 1.5);
        var first = await OpenNoteAsync(host, windows, 0);
        await Button(first, "Zoom to 400 %").ClickAsync();
        await Expect(first.Page.GetByText("Zoom:")).ToHaveTextAsync("Zoom: 200 %");
        var limited = await StateAsync(first.Window, state => state.Zoom == 2);
        await first.Window.CloseAsync();
        var second = await OpenNoteAsync(host, windows, 1);
        var reopened = await StateAsync(second.Window, state => state.Zoom == 2);

        Assert.Equal(1.5, main.Zoom);
        Assert.Equal(2, limited.Zoom);
        Assert.Equal(2, reopened.Zoom);
        await Expect(second.Page.GetByText("Zoom:")).ToHaveTextAsync("Zoom: 200 %");
    }

    [Fact]
    public async Task Ctrl_plus_minus_and_zero_zoom_the_page_through_the_host_on_Linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("WebView2 zooms on its own keys, which a window test's synthetic input does not reach.");
        }

        await using var host = await HostPage.OpenAsync();
        var zoomLevel = host.Page.GetByText("Zoom:");

        await host.Page.Keyboard.PressAsync("Control+=");
        await Expect(zoomLevel).ToHaveTextAsync("Zoom: 110 %");
        await host.Page.Keyboard.PressAsync("Control+=");
        await Expect(zoomLevel).ToHaveTextAsync("Zoom: 125 %");
        await host.Page.Keyboard.PressAsync("Control+-");
        await Expect(zoomLevel).ToHaveTextAsync("Zoom: 110 %");
        await host.Page.Keyboard.PressAsync("Control+0");
        await Expect(zoomLevel).ToHaveTextAsync("Zoom: 100 %");
        var state = await host.App.Window.NativeStateAsync();

        Assert.Equal(1, state.Zoom);
    }

    private static SparLocator Button(SparAppWindow window, string name) =>
        window.Page.GetByRole(SparRole.Button, new() { Name = name, Exact = true });

    private static async Task<SparAppWindow> OpenNoteAsync(HostPage host, IWindows windows, int index)
    {
        _ = await windows.OpenAsync("note", """{"text":"zoomed","n":1}""", Token);
        var note = await host.App.WindowAsync("note", index, Token);
        await Expect(note.Page.GetByText("This window: /note")).ToBeVisibleAsync();
        return note;
    }

    private static async Task<SparTestWindowState> StateAsync(SparWindow window, Func<SparTestWindowState, bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var state = await window.NativeStateAsync();
            if (done(state) || DateTime.UtcNow > limit)
            {
                return state;
            }

            await Task.Delay(50, Token);
        }
    }
}
