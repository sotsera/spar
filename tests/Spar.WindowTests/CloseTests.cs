using Spar.Testing;
using static Spar.Testing.SparAssertions;
using static Spar.WindowTests.HostPage;

namespace Spar.WindowTests;

/// <summary>
/// Closing the window from a test (<c>app.Window.CloseAsync</c>) through the two-stage close (decision "Two-stage
/// close"; decision "Window tests: closing, session-end budget, test theme and any-character keys"): the page's guard,
/// then the backend's, each closing, keeping or asking, the answers a test gives in place of the native dialogs, a
/// second request that joins the first, a navigated page's late answer, and a page that does not answer.
/// </summary>
[Trait("Surface", "Window")]
public sealed class CloseTests
{
    private const string PageQuestion = "Close without saving?";

    [Fact]
    public async Task A_close_asks_the_page_then_the_backend_and_the_window_is_gone()
    {
        await using var host = await HostPage.OpenAsync();
        var activity = host.Activity;

        var result = await host.App.Window.CloseAsync();

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Null(result.Question);
        Assert.False(result.PageDidNotAnswer);
        Assert.Equal(["close user automated none"], NotesOf(activity));
        var request = Assert.Single(activity.CloseRequests);
        Assert.Equal((CloseReason.User, true), (request.Reason, request.IsAutomated));
        var closed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Page.GetByRole(SparRole.Button, new() { Name = "Echo" }).ClickAsync());
        Assert.Contains("window is closed", closed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_keeps_the_window_ends_the_request_before_the_backend_is_asked()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("keep");

        var result = await host.App.Window.CloseAsync(new() { Reason = CloseReason.App });

        Assert.Equal(CloseOutcome.Kept, result.Outcome);
        Assert.Null(result.Question);
        Assert.Equal(["close app automated keep"], host.GuardNotes);
        Assert.Empty(host.Activity.CloseRequests);
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        host.AssertClean();
    }

    [Fact]
    public async Task In_the_automated_mode_the_pages_question_counts_as_close_and_is_reported()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("ask");

        var result = await host.App.Window.CloseAsync();

        Assert.Equal((CloseOutcome.Closed, PageQuestion), (result.Outcome, result.Question));
        Assert.Single(host.Activity.CloseRequests);
    }

    [Fact]
    public async Task Testing_the_pages_prompt_the_tests_answer_keeps_or_closes()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("ask");

        var kept = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen });

        Assert.Equal((CloseOutcome.Kept, PageQuestion), (kept.Outcome, kept.Question));
        Assert.Empty(host.Activity.CloseRequests);
        Assert.Equal(["close user ask"], host.GuardNotes);

        var closed = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.CloseAnyway });

        Assert.Equal((CloseOutcome.Closed, PageQuestion), (closed.Outcome, closed.Question));
        Assert.False(Assert.Single(host.Activity.CloseRequests).IsAutomated);
    }

    [Fact]
    public async Task The_backends_question_comes_after_the_page_agreed()
    {
        await using var host = await HostPage.OpenAsync();
        host.Activity.BackendAnswer = CloseDecision.Ask("A copy is running. Close anyway?");

        var kept = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen });
        host.Activity.BackendAnswer = CloseDecision.Keep;
        var keptByBackend = await host.App.Window.CloseAsync();

        Assert.Equal((CloseOutcome.Kept, "A copy is running. Close anyway?"), (kept.Outcome, kept.Question));
        Assert.Equal((CloseOutcome.Kept, null), (keptByBackend.Outcome, keptByBackend.Question));
        Assert.Equal(["close user none", "close user automated none"], host.GuardNotes);
        Assert.Equal(2, host.Activity.CloseRequests.Count);
        host.AssertClean();
    }

    [Fact]
    public async Task A_page_guard_that_throws_lets_the_person_decide()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("throw");

        var kept = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen });

        Assert.Equal(CloseOutcome.Kept, kept.Outcome);
        Assert.Equal("The page could not check whether it is safe to close. Close anyway?", kept.Question);

        // The SDK logs before it answers, but the console message reaches the test through the page's DevTools events
        // and the answer through the bridge and the host: two channels with no order between them.
        await Waits.UntilAsync(
            () => host.Page.ConsoleMessages.Any(message => message.Type == "error" && message.Text.Contains("close guard threw", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "the guard's console error");
    }

    [Fact]
    public async Task A_second_request_joins_the_open_one()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("late-keep");

        var first = host.App.Window.CloseAsync();
        await Waits.UntilAsync(() => host.GuardNotes.Count == 1, TimeSpan.FromSeconds(5), "the page's guard to start");
        var second = host.App.Window.CloseAsync();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(CloseOutcome.Kept, result.Outcome));
        Assert.Single(host.GuardNotes);
        Assert.Empty(host.Activity.CloseRequests);
    }

    [Fact]
    public async Task A_late_answer_from_a_page_that_navigated_away_is_ignored()
    {
        await using var host = await HostPage.OpenAsync();
        var activity = host.Activity;
        await host.GuardAsync("late-keep");

        // The backend's guard takes a moment, so the navigation completes before the window closes.
        activity.BackendGuardDelay = TimeSpan.FromSeconds(2);
        var closing = host.App.Window.CloseAsync();
        await Waits.UntilAsync(() => host.GuardNotes.Count == 1, TimeSpan.FromSeconds(5), "the page's guard to start");
        await host.Page.GotoAsync("/other.html");
        var result = await closing;

        // The page that was asked is gone: its keep, a second later, reaches no open request, and the window closes.
        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Single(activity.CloseRequests);
    }

    [Fact]
    public async Task A_page_that_shows_a_dialog_of_its_own_is_waited_for()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("dialog");

        var closing = host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.CloseAnyway });
        var dialog = host.Page.GetByRole(SparRole.Alertdialog, new() { Name = "Discard the changes?" });
        await Expect(dialog).ToBeVisibleAsync();

        // Past the 5 s a silent guard gets: the page answers pings and says it shows a dialog, so nothing is asked.
        await Task.Delay(TimeSpan.FromSeconds(6.5), TestContext.Current.CancellationToken);
        Assert.False(closing.IsCompleted);
        await dialog.GetByRole(SparRole.Button, new() { Name = "Stay" }).ClickAsync();
        var result = await closing;

        Assert.Equal(CloseOutcome.Kept, result.Outcome);
        Assert.False(result.PageDidNotAnswer);
        Assert.Empty(host.Activity.CloseRequests);
    }

    [Fact]
    public async Task A_silent_guard_gets_wait_or_close_anyway_and_close_anyway_still_asks_the_backend()
    {
        await using var host = await HostPage.OpenAsync();
        var activity = host.Activity;
        await host.GuardAsync("silent");
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.CloseAnyway });

        Results.Append("close.txt", $"silent guard, Close anyway: closed {started.ElapsedMilliseconds} ms after the request");
        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.True(result.PageDidNotAnswer);
        Assert.InRange(started.Elapsed.TotalSeconds, 4.5, 12);
        Assert.False(Assert.Single(activity.CloseRequests).IsAutomated);
    }

    [Fact]
    public async Task In_the_automated_mode_a_silent_guard_is_closed_anyway()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("silent");

        var result = await host.App.Window.CloseAsync();

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.True(result.PageDidNotAnswer);
    }

    [Fact]
    public async Task A_hung_page_is_closed_anyway_and_its_renderer_ended()
    {
        await using var host = await HostPage.OpenAsync();
        var activity = host.Activity;
        await host.HangAsync();
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.CloseAnyway });
        Results.Append("close.txt", $"hung page, Close anyway: closed {started.ElapsedMilliseconds} ms after the request");

        // The page never ran its guard: the request waited in its blocked thread.
        Assert.Equal((CloseOutcome.Closed, true), (result.Outcome, result.PageDidNotAnswer));
        Assert.Empty(NotesOf(activity));
        Assert.Single(activity.CloseRequests);
    }

    [Fact]
    public async Task Wait_keeps_the_request_open_until_the_hung_page_answers()
    {
        await using var host = await HostPage.OpenAsync();
        await host.GuardAsync("keep");
        await host.HangAsync();

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await host.App.Window.CloseAsync(new() { Automated = false, Answer = CloseAnswer.KeepOpen });
        Results.Append("close.txt", $"hung page, Wait: kept by the guard {started.ElapsedMilliseconds} ms after the request");

        // Wait, at about 5 s; the page comes back after 12 s, reads the request and its guard keeps the window.
        Assert.Equal((CloseOutcome.Kept, true), (result.Outcome, result.PageDidNotAnswer));
        Assert.Equal(["close user keep"], host.GuardNotes);
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
    }

    [Fact]
    public async Task A_session_end_runs_both_guards_with_its_reason_and_nothing_keeps_the_window()
    {
        await using var host = await HostPage.OpenAsync();
        var activity = host.Activity;
        await host.GuardAsync("keep");
        activity.BackendAnswer = CloseDecision.Keep;

        var result = await host.App.Window.CloseAsync(new() { Reason = CloseReason.SessionEnd, Automated = false, Answer = CloseAnswer.KeepOpen });

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Equal(["close sessionEnd keep"], NotesOf(activity));
        Assert.Equal(CloseReason.SessionEnd, Assert.Single(activity.CloseRequests).Reason);
    }

    [Fact]
    public async Task A_prompt_test_without_an_answer_is_refused_before_anything_is_asked()
    {
        await using var host = await HostPage.OpenAsync();

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => host.App.Window.CloseAsync(new() { Automated = false }));

        Assert.Contains("needs Answer", refused.Message, StringComparison.Ordinal);
        Assert.Empty(host.GuardNotes);
    }
}
