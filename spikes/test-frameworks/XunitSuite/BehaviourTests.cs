using System.Threading.Channels;

namespace Spike.XunitSuite;

public sealed class BehaviourTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Delay_observes_the_test_token()
    {
        var token = TestContext.Current.CancellationToken;
        Assert.True(token.CanBeCanceled);
        await Task.Delay(20, token);
    }

    [Fact(Timeout = 5_000)]
    public async Task Channel_read_observes_the_test_token()
    {
        var channel = Channel.CreateUnbounded<int>();
        _ = Task.Run(async () =>
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            await channel.Writer.WriteAsync(42, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(42, await channel.Reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Linked_token_cancels_with_a_timeout()
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        linked.CancelAfter(10);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.Infinite, linked.Token));
    }

    [Fact(Skip = "Demonstrates a statically skipped test.")]
    public void Skipped_statically() => Assert.Fail("A skipped test never runs.");

    [Fact]
    public void Writes_output()
    {
        output.WriteLine("SPIKE-OUTPUT via ITestOutputHelper");
        Console.WriteLine("SPIKE-OUTPUT via Console.WriteLine");
        TestContext.Current.SendDiagnosticMessage("SPIKE-OUTPUT via SendDiagnosticMessage");
        Assert.Contains("SPIKE-OUTPUT", output.Output, StringComparison.Ordinal);
    }
}
