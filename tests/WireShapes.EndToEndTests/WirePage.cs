using System.Text.Json;
using Microsoft.Playwright;
using Spar.Testing;

namespace WireShapes.EndToEndTests;

/// <summary>One app with its page open in a fresh browser context, and what the page reported.</summary>
internal sealed class WirePage : IAsyncDisposable
{
    private readonly List<string> _consoleErrors = [];

    private WirePage(SparTestApp app, IBrowserContext context, IPage page, string token)
    {
        App = app;
        Context = context;
        Page = page;
        Token = token;
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
            {
                lock (_consoleErrors)
                {
                    _consoleErrors.Add($"{message.Type}: {message.Text}");
                }
            }
        };
        page.PageError += (_, error) =>
        {
            lock (_consoleErrors)
            {
                _consoleErrors.Add($"page error: {error}");
            }
        };
    }

    public SparTestApp App { get; }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    public string Token { get; }

    /// <summary>The console's errors and warnings so far, and uncaught page errors.</summary>
    public IReadOnlyList<string> ConsoleErrors
    {
        get
        {
            lock (_consoleErrors)
            {
                return [.. _consoleErrors];
            }
        }
    }

    public static async Task<WirePage> OpenAsync(SharedBrowser shared, string? contracts = null)
    {
        var app = await SparTestApp.StartAsync<Program>(o =>
        {
            o.Surface = SparTestSurface.Browser;
            o.Page(Repository.Page).Page(Repository.Client, "spar/app/");
        });

        var context = await shared.Browser.NewContextAsync();
        // Without an override the page uses the fingerprint its generated package was built with.
        var url = app.PageUrl(contracts is null ? "/" : $"/?contracts={contracts}");
        var wire = new WirePage(app, context, await context.NewPageAsync(), url.Fragment["#token=".Length..]);
        await wire.Page.GotoAsync(url.ToString());
        await wire.WaitForReadyAsync();
        return wire;
    }

    public Task WaitForReadyAsync() => Assertions.Expect(Page.Locator("#status")).ToHaveTextAsync("ready");

    /// <summary>
    /// Runs one of the page's functions and returns its plain JSON result. Evaluation has no deadline of its own, and
    /// a call the host dropped is never answered, so the test sets one.
    /// </summary>
    public async Task<string> CallAsync(string function, object? argument)
    {
        var evaluation = Page.EvaluateAsync<JsonElement>($"async arg => JSON.stringify(await ({function})(arg))", argument);
        var result = await evaluation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return result.GetString()!;
    }

    /// <summary>Nothing dropped or refused by the bridge, no CSP violation, and nothing on the page's console.</summary>
    public Task AssertCleanAsync()
    {
        Assert.Empty(App.Problems);
        lock (_consoleErrors)
        {
            Assert.Empty(_consoleErrors);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await App.DisposeAsync();
    }
}
