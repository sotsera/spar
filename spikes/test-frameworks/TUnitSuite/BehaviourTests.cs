using System.Threading.Channels;
using TUnit.Core.Logging;

namespace Spike.TUnitSuite;

public sealed class BehaviourTests
{
    [Test]
    public async Task Delay_observes_the_test_token()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await Assert.That(token.CanBeCanceled).IsTrue();
        await Task.Delay(20, token);
    }

    // TUnit also injects the test's token as a parameter; [Timeout] cancels it.
    [Test]
    [Timeout(5_000)]
    public async Task Channel_read_observes_the_test_token(CancellationToken token)
    {
        var channel = Channel.CreateUnbounded<int>();
        _ = Task.Run(async () =>
        {
            await Task.Delay(10, token);
            await channel.Writer.WriteAsync(42, token);
        }, token);

        await Assert.That(await channel.Reader.ReadAsync(token)).IsEqualTo(42);
    }

    [Test]
    public async Task Linked_token_cancels_with_a_timeout()
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        linked.CancelAfter(10);
        await Assert.That(() => Task.Delay(Timeout.Infinite, linked.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    [Skip("Demonstrates a statically skipped test.")]
    public void Skipped_statically() => throw new InvalidOperationException("A skipped test never runs.");

    [Test]
    public async Task Writes_output()
    {
        var context = TestContext.Current!;
        context.Output.StandardOutput.WriteLine("SPIKE-OUTPUT via TestContext.Output.StandardOutput");
        Console.WriteLine("SPIKE-OUTPUT via Console.WriteLine");
        await context.GetDefaultLogger().LogInformationAsync("SPIKE-OUTPUT via GetDefaultLogger");
        await Assert.That(context.Output.GetStandardOutput()).Contains("SPIKE-OUTPUT");
    }
}
