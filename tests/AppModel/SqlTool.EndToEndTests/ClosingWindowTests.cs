using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace SqlTool.EndToEndTests;

[Trait("Surface", "Window")]
public sealed class ClosingWindowTests
{
    [Fact]
    public async Task Closing_with_an_unsaved_query_asks_first_and_tests_close_without_asking()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var query = app.Page.GetByRole(SparRole.Textbox, new() { Name = "Query" });
        await query.FillAsync("select name from sys.databases");

        // Testing the guard's prompt: the test answers in place of the native confirmation.
        var kept = await app.Window.CloseAsync(new()
        {
            Automated = false,
            Answer = CloseAnswer.KeepOpen,
        });
        Assert.Equal(CloseOutcome.Kept, kept.Outcome);
        Assert.Equal("The query has unsaved changes. Close without saving?", kept.Question);
        await Expect(query).ToHaveValueAsync("select name from sys.databases");

        // Automated, as tests and agents close the app: the guard saves, and the window closes.
        var result = await app.Window.CloseAsync();
        Assert.Equal(CloseOutcome.Closed, result.Outcome);
    }
}
