using Microsoft.Playwright;
using Spar.Testing;
using static Microsoft.Playwright.Assertions;

namespace MyApp.EndToEndTests;

/// <summary>The app's own page in Chromium against its real backend, which runs in this process.</summary>
[Trait("Surface", "Browser")]
public sealed class GreetingBrowserTests(SharedBrowser shared) : IClassFixture<SharedBrowser>
{
    [Fact]
    public async Task The_page_greets_a_name_through_the_backend()
    {
        await using var app = await SparTestApp.StartAsync<Program>(o =>
        {
            o.Surface = SparTestSurface.Browser;
            o.Page();
        }, TestContext.Current.CancellationToken);
        await using var context = await shared.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.PageUrl().ToString());
        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole(AriaRole.Button, new() { Name = "Greet" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Hello, Ada!");
        Assert.Empty(app.Problems);
    }
}
