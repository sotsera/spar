using System.Text;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>Dispatch outcomes (decision "<c>CallError</c> kinds"): what ran, what failed, and what was cancelled.</summary>
public sealed class DispatcherTests : IAsyncLifetime
{
    private TestBackend _backend = null!;

    public ValueTask InitializeAsync()
    {
        _backend = TestBackend.Create();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _backend.DisposeAsync();

    [Fact]
    public async Task An_unknown_command_is_rejected_before_anything_runs()
    {
        var outcome = await InvokeAsync("pages", "rename");

        Assert.Equal(("rejected", "unknown-command", "notStarted"), Failure(outcome));
    }

    [Fact]
    public async Task A_command_that_throws_is_failed_with_an_unknown_outcome()
    {
        var outcome = await InvokeAsync("boom", "throw");

        Assert.Equal(("failed", "failed", "unknown"), Failure(outcome));
        Assert.Equal("IOException: disk full", outcome.Failure!.Detail);
    }

    [Fact]
    public async Task Another_tokens_cancellation_is_not_a_confirmation()
    {
        var outcome = await InvokeAsync("boom", "otherToken");

        Assert.Equal(("failed", "failed", "unknown"), Failure(outcome));
    }

    [Fact]
    public async Task Cancelling_the_call_token_is_confirmed_as_stopped()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var outcome = await _backend.Dispatcher.InvokeAsync("slow", "wait", 1, ReadOnlyMemory<byte>.Empty, cancel.Token);

        Assert.Equal(("cancelled", "cancelled", "stopped"), Failure(outcome));
    }

    [Fact]
    public async Task The_commands_own_time_limit_ends_it_as_cancelled()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var outcome = await InvokeAsync("slow", "limited");

        Assert.Equal(("cancelled", "cancelled", "stopped"), Failure(outcome));
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.9, 5);
    }

    [Fact]
    public async Task The_command_sees_its_own_call()
    {
        var outcome = await _backend.Dispatcher.InvokeAsync("call", "describe", 42, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);

        Assert.Equal("\"call.describe#42\"", Encoding.UTF8.GetString(outcome.Payload.Span));
    }

    private ValueTask<SparOutcome> InvokeAsync(string contract, string command) =>
        _backend.Dispatcher.InvokeAsync(contract, command, 1, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);

    private static (string, string, string) Failure(SparOutcome outcome)
    {
        Assert.Equal(SparOutcomeKind.Failure, outcome.Kind);
        return (outcome.Failure!.Kind, outcome.Failure.Code, outcome.Failure.Execution);
    }
}
