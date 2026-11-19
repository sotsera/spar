using Microsoft.Extensions.DependencyInjection;
using Notebook.Pages;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>
/// Closing the Notebook's window through its close request (<c>app.Window.CloseAsync</c>, <c>docs/app-model.md</c>
/// 6.11): the page saves a draft that is not saved yet before it lets the window close, and asks before losing one that
/// cannot be saved; the person's answer is the test's.
/// </summary>
[Trait("Surface", "Window")]
public sealed class CloseJourneyTests
{
    private const string LostQuestion = "This page was deleted elsewhere. Close without your last changes?";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Closing_right_after_typing_saves_the_last_words_first()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Groceries");
        await Expect(notebook.Editor).ToHaveValueAsync("# Groceries\n");

        // Closed before the autosave's pause is over: the page's close guard saves the draft itself.
        await notebook.TypeAtEndAsync("\n- bread");
        var result = await notebook.App.Window.CloseAsync();

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Null(result.Question);
        var file = await File.ReadAllTextAsync(Path.Combine(notebook.App.Folders.Data, "pages", "groceries.md"), Token);
        Assert.EndsWith("# Groceries\n\n- bread", file.TrimEnd('\n'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_draft_that_cannot_be_saved_asks_before_the_window_closes()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Draft");
        await Expect(notebook.Editor).ToHaveValueAsync("# Draft\n");

        // The page goes away behind the window's back, so the next save of the draft fails.
        Assert.True(await notebook.App.Services.GetRequiredService<IPageStore>().DeleteAsync("draft", Token));
        await notebook.TypeAtEndAsync("\nlast thoughts");
        await notebook.ExpectStatusAsync("This page was deleted elsewhere.");

        var kept = await notebook.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen });

        Assert.Equal((CloseOutcome.Kept, LostQuestion), (kept.Outcome, kept.Question));
        await Expect(notebook.Editor).ToHaveValueAsync("# Draft\n\nlast thoughts");
        await notebook.ScreenshotAsync("close-kept");

        var closed = await notebook.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.CloseAnyway });

        Assert.Equal((CloseOutcome.Closed, LostQuestion), (closed.Outcome, closed.Question));
    }

    [Fact]
    public async Task In_the_automated_mode_an_unsaved_draft_does_not_hold_the_window()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Draft");
        await Expect(notebook.Editor).ToHaveValueAsync("# Draft\n");
        Assert.True(await notebook.App.Services.GetRequiredService<IPageStore>().DeleteAsync("draft", Token));
        await notebook.TypeAtEndAsync("\nnever saved");
        await notebook.ExpectStatusAsync("This page was deleted elsewhere.");

        // The automated mode: the guard sees automated and closes; nothing is asked of anyone.
        var result = await notebook.App.Window.CloseAsync();

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Null(result.Question);
    }
}
