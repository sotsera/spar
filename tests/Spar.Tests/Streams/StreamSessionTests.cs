using System.Text.Json;
using Spar.Bridge;

namespace Spar.Tests.Streams;

/// <summary>
/// Streams and subscriptions through the session (plan section 2c; decision "M3a's decide-first questions"): credits in
/// items, batches, the in-band end, one terminal message after the last items, cancellation, and the subscription buffer.
/// </summary>
public sealed class StreamSessionTests
{
    [Fact]
    public async Task A_stream_sends_every_item_then_ends()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "count", credits: 256, "100"));
        var (items, batches, end) = await ReadToEndAsync(bridge.Sent);

        Assert.Equal(Enumerable.Range(0, 100), items);
        Assert.All(batches, batch => Assert.True(Rows(batch).Count <= BridgeStream.BatchItems));
        Assert.Equal(BridgeMessageType.End, end.Type);
        Assert.True(end.Ok);
        Assert.Equal(1, end.Id);
    }

    [Fact]
    public async Task The_producer_waits_while_the_page_has_no_credit()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "count", credits: 10, "25"));
        var first = await ReadItemsAsync(bridge.Sent, 10);
        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(100)));

        bridge.Session.Receive(Credits(1, 10));
        var second = await ReadItemsAsync(bridge.Sent, 10);
        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(100)));

        bridge.Session.Receive(Credits(1, 10));
        var (rest, _, end) = await ReadToEndAsync(bridge.Sent);

        Assert.Equal(Enumerable.Range(0, 25), [.. first, .. second, .. rest]);
        Assert.True(end.Ok);
    }

    [Fact]
    public async Task An_exception_after_items_fails_the_stream_after_them_with_their_count()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "failAfter", credits: 256, ""));
        var (items, _, end) = await ReadToEndAsync(bridge.Sent);

        Assert.Equal([0, 1, 2], items);
        Assert.Equal(BridgeMessageType.Failure, end.Type);
        Assert.Equal(("failed", "failed", "unknown"), (end.Kind, end.Code, end.Execution));
        Assert.Equal(3, end.ItemsSent);
    }

    [Fact]
    public async Task A_declared_error_ends_the_stream_in_band()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "errorAfter", credits: 256, ""));
        var (items, _, end) = await ReadToEndAsync(bridge.Sent);

        Assert.Equal([0, 1], items);
        Assert.Equal(BridgeMessageType.End, end.Type);
        Assert.False(end.Ok);
        Assert.Equal("{\"code\":\"tooMany\"}", System.Text.Encoding.UTF8.GetString(end.Payload.Span));
    }

    [Fact]
    public async Task A_cancel_stops_a_producer_that_ignores_its_token_and_is_confirmed()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "forever", credits: 1_000_000, ""));
        await ReadItemsAsync(bridge.Sent, 10);
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":1}\t");
        var (_, _, end) = await ReadToEndAsync(bridge.Sent);

        Assert.Equal(("cancelled", "cancelled", "stopped"), (end.Kind, end.Code, end.Execution));
        Assert.True(end.ItemsSent >= 10);
    }

    [Fact]
    public async Task An_item_that_cannot_be_written_stops_the_stream_with_a_response_failure()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "badItem", credits: 256, ""));
        var (items, _, end) = await ReadToEndAsync(bridge.Sent);

        Assert.Equal([0, 1], items);
        Assert.Equal(("response", "serialization", "stopped"), (end.Kind, end.Code, end.Execution));
        Assert.Equal(2, end.ItemsSent);
    }

    [Fact]
    public async Task A_subscription_drops_its_oldest_values_and_reports_the_gap()
    {
        await using var bridge = await TestBridge.OpenAsync();

        // Capacity 4: values 2 to 15 are dropped while the page holds no credit, and 16 to 19 wait in the buffer.
        bridge.Session.Receive(Stream(1, "feed", "ticks", credits: 2, "20"));
        var first = await ReadItemsAsync(bridge.Sent, 2);
        await WaitForAsync(() => TestStreams.Reached.TryDequeue(out _));

        // Past the batching window, so the credit comes after a quiet moment: the drained buffer still goes as one batch.
        await Task.Delay(BridgeStream.Window * 3, TestContext.Current.CancellationToken);
        bridge.Session.Receive(Credits(1, 10));
        var after = await bridge.Sent.NextAsync();

        Assert.Equal([0, 1], first);
        Assert.Equal(BridgeMessageType.Items, after.Type);
        Assert.Equal(14, after.Dropped);
        Assert.Equal([16, 17, 18, 19], Rows(after));
    }

    [Fact]
    public async Task A_latest_only_subscription_sends_only_its_newest_value_with_no_gap()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "feed", "latest", credits: 1, "50"));
        var first = await ReadItemsAsync(bridge.Sent, 1);
        await WaitForAsync(() => TestStreams.Reached.TryDequeue(out _));

        bridge.Session.Receive(Credits(1, 5));
        var after = await bridge.Sent.NextAsync();

        Assert.Equal([0], first);
        Assert.Null(after.Dropped);
        Assert.Equal([49], Rows(after));
        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task A_unary_command_is_not_a_stream_and_a_stream_is_not_a_call()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "pages", "get", credits: 256, "{}"));
        var asStream = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(2, "rows", "count", "3"));
        var asCall = await bridge.Sent.NextAsync();

        Assert.Equal(("rejected", "invalid-request", 0L), (asStream.Kind, asStream.Code, asStream.ItemsSent));
        Assert.Equal(("rejected", "invalid-request"), (asCall.Kind, asCall.Code));
    }

    [Fact]
    public async Task Streams_past_the_limit_are_refused_as_busy_and_keep_the_call_slots_free()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { StreamsOpen = 1, CallsInFlight = 1 });

        bridge.Session.Receive(Stream(1, "feed", "ticks", credits: 256, "0"));
        await WaitForAsync(() => TestStreams.Reached.TryDequeue(out _));
        bridge.Session.Receive(Stream(2, "feed", "ticks", credits: 256, "0"));
        var refused = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(3, "echo", "back", "1"));
        var answered = await bridge.Sent.NextAsync();

        Assert.Equal((2L, "rejected", "busy"), (refused.Id, refused.Kind, refused.Code));
        Assert.Equal((3L, BridgeMessageType.Result), (answered.Id, answered.Type));
    }

    [Fact]
    public async Task Closing_the_document_stops_a_producer_waiting_for_credits()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(Stream(1, "rows", "count", credits: 1, "10"));
        await ReadItemsAsync(bridge.Sent, 1);
        bridge.Session.Close();

        await bridge.Session.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(50)));
    }

    internal static string Stream(long id, string contract, string command, int credits, string payload) =>
        $"{{\"type\":\"stream\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"{command}\",\"credits\":{credits}}}\t{payload}";

    internal static string Credits(long id, int credits) => $"{{\"type\":\"credits\",\"id\":{id},\"credits\":{credits}}}\t";

    internal static List<int> Rows(BridgeMessage items)
    {
        using var document = JsonDocument.Parse(items.Payload);
        return [.. document.RootElement.EnumerateArray().Select(row => row.GetProperty("n").GetInt32())];
    }

    private static async Task<List<int>> ReadItemsAsync(SentMessages sent, int count)
    {
        var rows = new List<int>();
        while (rows.Count < count)
        {
            var message = await sent.NextAsync();
            Assert.Equal(BridgeMessageType.Items, message.Type);
            rows.AddRange(Rows(message));
        }

        return rows;
    }

    private static async Task<(List<int> Items, List<BridgeMessage> Batches, BridgeMessage End)> ReadToEndAsync(SentMessages sent)
    {
        var rows = new List<int>();
        var batches = new List<BridgeMessage>();
        while (true)
        {
            var message = await sent.NextAsync();
            if (message.Type != BridgeMessageType.Items)
            {
                return (rows, batches, message);
            }

            batches.Add(message);
            rows.AddRange(Rows(message));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
