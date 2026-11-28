using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spar.Hosting;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The page-drawn title bar in real windows (<c>docs/app-model.md</c> 6.9; plan 3, "Title bar drawn by the page"):
/// WindowHost's kind <c>titled</c> sets <c>TitleBar(TitleBar.Custom)</c>, and its page has a bar with a name, a search
/// box and an empty <c>data-spar-caption</c>. The SDK draws the named buttons, posts the layout, and the host hit-tests
/// from it: on Windows the caption window answers the buttons and the top resize band and the window the drag area; on
/// Linux the view finds the same parts. The page's own click on a button runs its action. The real drags on Linux are
/// <see cref="TitleBarDragTests"/>, which run alone. Not checked here: Windows' own
/// moves, Snap Layouts and the system menu, which need real input (the Sandbox's user-session lane), and maximizing,
/// which neither test display does.
/// </summary>
[Trait("Surface", "Window")]
public sealed class TitleBarTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Prefix => OperatingSystem.IsWindows() ? "sink" : "view";

    [Fact]
    public async Task The_sdk_draws_named_buttons_and_the_host_hit_tests_the_pages_layout()
    {
        await using var host = await HostPage.OpenAsync();
        var titled = await OpenTitledAsync(host);
        var page = titled.Page;

        await Expect(Caption(page, "Minimize")).ToBeVisibleAsync();
        await Expect(Caption(page, "Maximize")).ToBeVisibleAsync();
        await Expect(Caption(page, "Close")).ToBeVisibleAsync();
        var state = await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-spar-window')");
        var buttons = await CentresAsync(page, "[data-spar-caption-button]");
        var search = (await CentresAsync(page, "#titlebar-search"))[0];
        var space = (await CentresAsync(page, ".titlebar-space"))[0];
        var close = await HitWhenLaidOutAsync(titled.Window, buttons[2]);

        var minimize = await titled.Window.TitleBarHitAsync(buttons[0].X, buttons[0].Y);
        var maximize = await titled.Window.TitleBarHitAsync(buttons[1].X, buttons[1].Y);
        var drag = await titled.Window.TitleBarHitAsync(space.X, space.Y);
        var control = await titled.Window.TitleBarHitAsync(search.X, search.Y);
        var below = await titled.Window.TitleBarHitAsync(space.X, 200);
        var edge = await titled.Window.TitleBarHitAsync(space.X, 2);

        Assert.Equal("normal", state);
        Assert.Equal($"{Prefix}:close", close);
        Assert.Equal($"{Prefix}:minimize", minimize);
        Assert.Equal($"{Prefix}:maximize", maximize);
        Assert.Equal(OperatingSystem.IsWindows() ? "window:caption" : "view:drag", drag);
        Assert.Equal(OperatingSystem.IsWindows() ? "window:client" : "view:client", control);
        Assert.Equal(OperatingSystem.IsWindows() ? "window:client" : "view:client", below);

        // Windows' top resize band sits over the bar. On Linux the test display has no compositor, so the window has no
        // frame and the view resizes it from its own edges; under a compositor GTK's frame keeps them outside the page.
        Assert.Equal(OperatingSystem.IsWindows() ? "sink:top" : "view:top", edge);
    }

    [Fact]
    public async Task The_pages_own_click_on_a_caption_button_runs_its_action()
    {
        await using var host = await HostPage.OpenAsync();
        var titled = await OpenTitledAsync(host);

        if (OperatingSystem.IsWindows())
        {
            // Minimizing is a request to the window manager on Linux, and the test display has none.
            await Caption(titled.Page, "Minimize").ClickAsync();
            var minimized = await StateAsync(titled.Window, state => state.Minimized);
            Assert.True(minimized.Minimized);

            // Restored before the next click: a minimized page draws no frames, which the page's input waits for.
            host.App.Services.GetRequiredService<IWindows>().All.Single(window => window.Kind == "titled").Restore();
            await StateAsync(titled.Window, state => !state.Minimized);
        }

        await Caption(titled.Page, "Close").ClickAsync();
        await titled.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    internal static SparLocator Caption(SparPage page, string name) =>
        page.Locator("#titlebar").GetByRole(SparRole.Button, new() { Name = name, Exact = true });

    internal static async Task<SparAppWindow> OpenTitledAsync(HostPage host)
    {
        await host.ClickAsync("Open a titled window");
        var titled = await host.App.WindowAsync("titled", cancellationToken: Token);
        await Expect(titled.Page.GetByText("This window: /titled")).ToBeVisibleAsync();
        await Expect(Caption(titled.Page, "Close")).ToBeVisibleAsync();
        return titled;
    }

    /// <summary>The centres of the elements <paramref name="selector"/> finds, in CSS pixels of the viewport.</summary>
    internal static async Task<IReadOnlyList<(float X, float Y)>> CentresAsync(SparPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>(
            $"JSON.stringify([...document.querySelectorAll('{selector}')].map(e => {{ const r = e.getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }}))");
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(point => (point[0].GetSingle(), point[1].GetSingle()))];
    }

    /// <summary>What the host answers at the point once the page's layout has reached it, which the SDK posts a frame later.</summary>
    internal static async Task<string> HitWhenLaidOutAsync(SparWindow window, (float X, float Y) point)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var hit = await window.TitleBarHitAsync(point.X, point.Y);
            if (!hit.EndsWith(":client", StringComparison.Ordinal) || DateTime.UtcNow > limit)
            {
                return hit;
            }

            await Task.Delay(50, Token);
        }
    }

    internal static async Task<SparTestWindowState> StateAsync(SparWindow window, Func<SparTestWindowState, bool> done)
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
