using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Pantry.EndToEndTests;

/// <summary>The evaluation's hidden check: the greeting is drawn inside the window, at its size.</summary>
[Trait("Surface", "Window")]
public sealed class EvalGreetingInViewWindowTests
{
    [Fact]
    public async Task The_greeting_shows_inside_the_window()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var page = app.Page;

        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole(SparRole.Button, new() { Name = "Greet" }).ClickAsync();
        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Hello, Ada!");

        // The status's box inside the viewport, and no ancestor clipping it away.
        var inView = await page.EvaluateAsync(
            "(() => { const status = document.querySelector('[role=\"status\"]'); const box = status.getBoundingClientRect(); " +
            "if (box.height === 0 || box.top < 0 || box.bottom > innerHeight || box.left < 0 || box.right > innerWidth) { return false; } " +
            "for (let e = status.parentElement; e; e = e.parentElement) { const clip = e.getBoundingClientRect(); " +
            "if (getComputedStyle(e).overflow !== 'visible' && (box.bottom > clip.bottom || box.top < clip.top)) { return false; } } return true; })()");

        Assert.True(inView.GetBoolean(), "The greeting is outside the window.");
    }
}
