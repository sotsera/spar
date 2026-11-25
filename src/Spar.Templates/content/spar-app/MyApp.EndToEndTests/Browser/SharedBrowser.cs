using Microsoft.Playwright;

namespace MyApp.EndToEndTests;

/// <summary>
/// One headless Chromium for the browser tests of a class; each test opens a context of its own. A class fixture, so a
/// run of only the window or black-box tests starts no browser.
/// </summary>
public sealed class SharedBrowser : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("The browser has not started.");

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
