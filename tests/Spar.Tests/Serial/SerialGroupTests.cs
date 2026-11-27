using Spar.Bridge;

namespace Spar.Tests.Serial;

/// <summary>
/// Serial groups through the session and the dispatcher (decision "M3b's decide-first questions", item 1): one call of
/// a group at a time, in arrival order, app-wide; a waiting call holds no call slot, counts in the queue, can be
/// cancelled before it starts, and its time limit starts when it runs.
/// </summary>
public sealed class SerialGroupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_calls_of_a_group_run_one_at_a_time_in_arrival_order()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Hold(1, "order-a"));
        bridge.Session.Receive(Hold(2, "order-b"));
        bridge.Session.Receive(Hold(3, "order-c"));
        await WaitForAsync("order-a");
        Assert.False(await StartsWithinAsync("order-b", TimeSpan.FromMilliseconds(100)));

        TestSerial.Release("order-a");
        await WaitForAsync("order-b");
        TestSerial.Release("order-b");
        await WaitForAsync("order-c");
        TestSerial.Release("order-c");
        var answered = new[] { await bridge.Sent.NextAsync(), await bridge.Sent.NextAsync(), await bridge.Sent.NextAsync() };

        Assert.Equal([1L, 2L, 3L], answered.Select(message => message.Id!.Value));
        Assert.All(answered, message => Assert.True(message.Ok));
    }

    [Fact]
    public async Task A_waiting_call_holds_no_call_slot()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { CallsInFlight = 1 });

        bridge.Session.Receive(Hold(1, "slot-a"));
        await WaitForAsync("slot-a");
        bridge.Session.Receive(Hold(2, "slot-b"));
        bridge.Session.Receive(TestBridge.Call(3, "echo", "back", "\"free\""));
        var echoed = await bridge.Sent.NextAsync();

        Assert.Equal((3L, BridgeMessageType.Result), (echoed.Id, echoed.Type));
        TestSerial.Release("slot-a");
        await WaitForAsync("slot-b");
        TestSerial.Release("slot-b");
    }

    [Fact]
    public async Task A_call_cancelled_while_it_waits_never_runs()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Hold(1, "cancel-a"));
        await WaitForAsync("cancel-a");
        bridge.Session.Receive(Hold(2, "cancel-b"));
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":2}\t");
        var cancelled = await bridge.Sent.NextAsync();
        TestSerial.Release("cancel-a");
        var first = await bridge.Sent.NextAsync();

        Assert.Equal((2L, "cancelled", "cancelled-before-start", "notStarted"), (cancelled.Id, cancelled.Kind, cancelled.Code, cancelled.Execution));
        Assert.Equal((1L, true), (first.Id, first.Ok));
        Assert.DoesNotContain("cancel-b", TestSerial.Started);
    }

    [Fact]
    public async Task Waiting_calls_count_in_the_queue_and_past_it_are_busy()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { QueuedCalls = 1 });

        bridge.Session.Receive(Hold(1, "queue-a"));
        await WaitForAsync("queue-a");
        bridge.Session.Receive(Hold(2, "queue-b"));
        bridge.Session.Receive(Hold(3, "queue-c"));
        var refused = await bridge.Sent.NextAsync();

        Assert.Equal((3L, "rejected", "busy"), (refused.Id, refused.Kind, refused.Code));
        TestSerial.Release("queue-a");
        await WaitForAsync("queue-b");
        TestSerial.Release("queue-b");
    }

    [Fact]
    public async Task The_time_limit_starts_when_the_call_runs()
    {
        await using var bridge = await TestBridge.OpenAsync();

        // The limited command has a 1 s limit; it waits 1.5 s for its turn and still starts.
        bridge.Session.Receive(Hold(1, "limit-a"));
        await WaitForAsync("limit-a");
        bridge.Session.Receive($"{{\"type\":\"call\",\"id\":2,\"contract\":\"serial\",\"command\":\"limited\"}}\t\"limit-b\"");
        await Task.Delay(1_500, Token);
        TestSerial.Release("limit-a");

        await WaitForAsync("limit-b");
        TestSerial.Release("limit-b");
        var first = await bridge.Sent.NextAsync();
        var limited = await bridge.Sent.NextAsync();

        // Had the limit counted the wait, the command would have started on an expired token and ended cancelled.
        Assert.Equal((1L, true), (first.Id, first.Ok));
        Assert.Equal((2L, BridgeMessageType.Result, true), (limited.Id, limited.Type, limited.Ok));
    }

    [Fact]
    public async Task Two_documents_share_one_group()
    {
        await using var bridge = await TestBridge.OpenAsync();
        var other = new SentMessages();
        using var second = new BridgeSession(bridge.Backend.Dispatcher, TestBackend.Fingerprint, "doc-2", other.Add, includeDetails: false);
        second.Receive(TestBridge.Hello);
        await other.NextAsync();

        bridge.Session.Receive(Hold(1, "shared-a"));
        await WaitForAsync("shared-a");
        second.Receive(Hold(1, "shared-b"));

        Assert.False(await StartsWithinAsync("shared-b", TimeSpan.FromMilliseconds(100)));
        TestSerial.Release("shared-a");
        await WaitForAsync("shared-b");
        TestSerial.Release("shared-b");
        await second.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    private static string Hold(long id, string name) =>
        $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"serial\",\"command\":\"hold\"}}\t\"{name}\"";

    private static async Task WaitForAsync(string name)
    {
        Assert.True(await StartsWithinAsync(name, TimeSpan.FromSeconds(10)), $"{name} never started");
    }

    private static async Task<bool> StartsWithinAsync(string name, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (TestSerial.Started.Contains(name))
            {
                return true;
            }

            await Task.Delay(5, Token);
        }

        return TestSerial.Started.Contains(name);
    }
}
