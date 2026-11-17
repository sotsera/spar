using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spar.Hosting;

namespace Spar.Tests;

/// <summary>The backend's close guard as every host runs it (decision "Two-stage close"; docs/app-model.md 6.11).</summary>
public sealed class CloseGuardTests
{
    private static readonly CloseRequest ByUser = new("main", CloseReason.User, IsAutomated: false);
    private static readonly CloseRequest Automated = new("main", CloseReason.User, IsAutomated: true);
    private static readonly CloseRequest SessionEnd = new("main", CloseReason.SessionEnd, IsAutomated: false);

    [Fact]
    public void A_user_close_gets_the_guards_answer()
    {
        Assert.Equal(CloseVerdict.Close, CloseGuard.Apply(ByUser, CloseDecision.Close));
        Assert.Equal(CloseVerdict.Keep, CloseGuard.Apply(ByUser, CloseDecision.Keep));
        Assert.Equal(new CloseVerdict(CloseVerdictKind.Ask, "Close?"), CloseGuard.Apply(ByUser, CloseDecision.Ask("Close?")));
    }

    [Fact]
    public void In_the_automated_mode_an_ask_counts_as_close_and_a_keep_still_keeps()
    {
        Assert.Equal(CloseVerdict.Close, CloseGuard.Apply(Automated, CloseDecision.Ask("Close?")));
        Assert.Equal(CloseVerdict.Keep, CloseGuard.Apply(Automated, CloseDecision.Keep));
    }

    [Fact]
    public void At_session_end_nothing_keeps_the_window_or_asks()
    {
        Assert.Equal(CloseVerdict.Close, CloseGuard.Apply(SessionEnd, CloseDecision.Keep));
        Assert.Equal(CloseVerdict.Close, CloseGuard.Apply(SessionEnd, CloseDecision.Ask("Close?")));
    }

    [Fact]
    public void An_ask_needs_a_question()
    {
        Assert.Throws<ArgumentException>(() => CloseDecision.Ask(" "));
        Assert.Equal(("Close", "Keep", "Ask(Sure?)"), (CloseDecision.Close.ToString(), CloseDecision.Keep.ToString(), CloseDecision.Ask("Sure?").ToString()));
    }

    [Fact]
    public async Task Without_a_guard_every_request_closes()
    {
        var guard = new CloseGuard(null, EmptyServices(), NullLogger.Instance);

        Assert.False(guard.HasGuard);
        Assert.Equal(CloseVerdict.Close, await guard.DecideAsync(ByUser, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_guard_runs_on_the_thread_pool_with_the_request_and_the_apps_services()
    {
        var services = EmptyServices();
        (CloseRequest Request, IServiceProvider Services, bool Pool)? seen = null;
        var guard = Guard((request, provider, _) =>
        {
            seen = (request, provider, Thread.CurrentThread.IsThreadPoolThread);
            return ValueTask.FromResult(CloseDecision.Keep);
        }, services);

        var verdict = await guard.DecideAsync(ByUser, TestContext.Current.CancellationToken);

        Assert.Equal(CloseVerdict.Keep, verdict);
        Assert.Equal((ByUser, services, true), seen);
    }

    [Fact]
    public async Task A_guard_that_fails_lets_the_user_decide()
    {
        var guard = Guard((_, _, _) => throw new InvalidOperationException("broken"));

        Assert.Equal(new CloseVerdict(CloseVerdictKind.Ask, CloseGuard.FailedGuardQuestion), await guard.DecideAsync(ByUser, TestContext.Current.CancellationToken));
        Assert.Equal(CloseVerdict.Close, await guard.DecideAsync(Automated, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task At_session_end_a_guard_that_does_not_answer_in_time_loses_its_say()
    {
        var guard = Guard(async (_, _, _) =>
        {
            // Ignores its token on purpose: the deadline must hold anyway.
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return CloseDecision.Keep;
        }, deadlineMilliseconds: 100);

        var verdict = await guard.DecideAsync(SessionEnd, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(CloseVerdict.Close, verdict);
    }

    [Fact]
    public async Task The_builder_keeps_the_guard_and_wraps_the_synchronous_form()
    {
        var spar = new SparBuilder([]);

        spar.OnCloseRequested((request, _) => request.IsAutomated ? CloseDecision.Close : CloseDecision.Keep);
        var decision = spar.CloseGuard!(ByUser, EmptyServices(), CancellationToken.None);

        Assert.True(decision.IsCompletedSuccessfully);
        Assert.Same(CloseDecision.Keep, await decision);
    }

    private static CloseGuard Guard(
        Func<CloseRequest, IServiceProvider, CancellationToken, ValueTask<CloseDecision>> guard,
        IServiceProvider? services = null,
        int deadlineMilliseconds = CloseGuard.SessionEndDeadlineMilliseconds) =>
        new(guard, services ?? EmptyServices(), NullLogger.Instance, deadlineMilliseconds);

    private static ServiceProvider EmptyServices() => new ServiceCollection().BuildServiceProvider();
}
