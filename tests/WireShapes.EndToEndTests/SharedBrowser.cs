using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(WireShapes.EndToEndTests.SharedBrowser))]

namespace WireShapes.EndToEndTests;

/// <summary>
/// One headless Chromium for the whole run; each test opens its own context, so cookies, storage and cache never cross
/// tests (plan section 5, level 4). Chromium keeps its profile in a temporary folder that it deletes when it closes.
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

/// <summary>Folders of this repository that the tests serve, found from the test's own output folder.</summary>
internal static class Repository
{
    private static readonly Lazy<string> Root = new(FindRoot);

    /// <summary>The WireShapes page.</summary>
    public static string Page => Path.Combine(Root.Value, "tests", "apps", "WireShapes", "page");

    /// <summary>The app's generated <c>@spar/app</c> package, which the page imports from <c>/spar/app/</c>.</summary>
    public static string Client
    {
        get
        {
            var folder = Path.Combine(Root.Value, "tests", "apps", "WireShapes", "obj", "spar", "client");
            return File.Exists(Path.Combine(folder, "package.json"))
                ? folder
                : throw new InvalidOperationException($"The WireShapes client is not generated: build tests/apps/WireShapes ({folder}).");
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}
