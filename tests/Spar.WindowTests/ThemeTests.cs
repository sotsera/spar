using Microsoft.Win32;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// Window tests are light by default (decision "Window tests: closing, session-end budget, test theme and
/// any-character keys"), so a screenshot is the same on every machine: the page's <c>prefers-color-scheme</c>, what
/// it draws, and the window's own colours (its title bar and the colour behind the transparent page) all follow
/// <c>o.ColorScheme</c>. A DevTools screenshot holds only the page, never the window's own colour, so that is read
/// from the window.
/// </summary>
[Trait("Surface", "Window")]
public sealed class ThemeTests
{
    private const uint White = 0xFFFFFF;
    private const uint DefaultDark = 0x1F1F1F;

    [Fact]
    public async Task A_window_test_is_light_by_default_whatever_the_machines_theme()
    {
        await using var host = await HostPage.OpenAsync();

        Assert.False(await PrefersDarkAsync(host));
        Assert.InRange(await ScreenshotBackgroundAsync(host), 240, 255);
        Assert.Equal((false, White), await host.App.Window.NativeThemeAsync());
        host.AssertClean();
    }

    [Fact]
    public async Task A_dark_window_test_gets_a_dark_page_and_a_dark_window()
    {
        await using var host = await HostPage.OpenAsync(options => options.ColorScheme = SparColorScheme.Dark);

        Assert.True(await PrefersDarkAsync(host));
        Assert.InRange(await ScreenshotBackgroundAsync(host), 0, 64);
        Assert.Equal((true, DefaultDark), await host.App.Window.NativeThemeAsync());
        host.AssertClean();
    }

    [Fact]
    public async Task The_scheme_lasts_across_a_navigation()
    {
        await using var host = await HostPage.OpenAsync(options => options.ColorScheme = SparColorScheme.Dark);

        await host.Page.GotoAsync("/other.html");

        Assert.True(await PrefersDarkAsync(host));
    }

    [Fact]
    public async Task System_follows_the_machines_theme()
    {
        var dark = OperatingSystem.IsWindows() && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
        await using var host = await HostPage.OpenAsync(options => options.ColorScheme = SparColorScheme.System);

        Assert.Equal(dark, await PrefersDarkAsync(host));
        Assert.Equal((dark, dark ? DefaultDark : White), await host.App.Window.NativeThemeAsync());
    }

    [Fact]
    public async Task Each_window_keeps_its_own_scheme_beside_another()
    {
        await using var light = await HostPage.OpenAsync();
        await using var dark = await HostPage.OpenAsync(options => options.ColorScheme = SparColorScheme.Dark);

        Assert.False(await PrefersDarkAsync(light));
        Assert.True(await PrefersDarkAsync(dark));
    }

    private static Task<bool> PrefersDarkAsync(HostPage host) =>
        host.Page.EvaluateAsync<bool>("matchMedia('(prefers-color-scheme: dark)').matches");

    /// <summary>The red of a pixel at the header's blank right end, where the page draws no colour of its own.</summary>
    private static async Task<int> ScreenshotBackgroundAsync(HostPage host)
    {
        var width = await host.Page.EvaluateAsync<int>("window.innerWidth");
        var (red, green, blue) = Png.Pixel(await host.Page.ScreenshotAsync(), width - 4, 4);
        Results.Append("theme.txt", $"{TestContext.Current.TestCase?.TestMethodName}: screenshot background ({red}, {green}, {blue})");
        return red;
    }
}
