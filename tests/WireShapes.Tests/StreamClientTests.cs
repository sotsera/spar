using Microsoft.Extensions.DependencyInjection;
using Spar;
using Spar.Testing;
using WireShapes;

namespace WireShapes.Tests;

/// <summary>
/// The typed test clients of streams and subscriptions (decision "M3a's decide-first questions", item 4), over the app's
/// real dispatch and the bridge's own batching, credits and buffer: values, the in-band end, the gap, the newest value
/// only, and the channel dropped and restored.
/// </summary>
public sealed class StreamClientTests : IAsyncLifetime
{
    private SparTestApp _app = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private StreamsClient Streams => new(_app);

    private StreamMarks Marks => _app.Services.GetRequiredService<StreamMarks>();

    public async ValueTask InitializeAsync() => _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task A_stream_delivers_every_row_in_order()
    {
        var expected = 0;
        await foreach (var row in Streams.RowsAsync(100_000, Token))
        {
            Assert.Equal(expected, row.N);
            expected++;
        }

        Assert.Equal(100_000, expected);
    }

    [Fact]
    public async Task A_declared_error_is_the_last_item()
    {
        var items = await Streams.ReadAsync(5, Token).ToListAsync(Token);

        Assert.Equal(6, items.Count);
        Assert.All(items.Take(5), item => Assert.True(item.IsSuccess));
        Assert.Equal(new RowsError.Unreadable(5), items[^1].Error);
    }

    [Fact]
    public async Task Leaving_the_loop_early_stops_the_stream()
    {
        var taken = 0;
        await foreach (var _ in Streams.RowsAsync(int.MaxValue, Token))
        {
            if (++taken == 10)
            {
                break;
            }
        }

        Assert.Equal(10, taken);
    }

    [Fact]
    public async Task A_subscription_that_falls_behind_reports_the_gap_before_the_newest_values()
    {
        // 256 credits take ticks 0 to 255; the buffer of 8 keeps 992 to 999 and drops the 736 between.
        var subscription = Streams.TicksAsync(1000, Token).GetAsyncEnumerator(Token);
        await using var _ = subscription;
        Assert.True(await subscription.MoveNextAsync());
        await Marks.ReachedAsync("ticks offered").WaitAsync(TimeSpan.FromSeconds(10), Token);

        var items = new List<SparSubscriptionItem<Tick>> { subscription.Current };
        while (items.Count < 256 + 1 + 8 && await subscription.MoveNextAsync())
        {
            items.Add(subscription.Current);
        }

        Assert.Equal(Enumerable.Range(0, 256).Select(n => (long)n), items.Take(256).Select(item => item.Value.N));
        Assert.Equal(new SparSubscriptionEvent.Gap(736), items[256].Event);
        Assert.Equal(Enumerable.Range(992, 8).Select(n => (long)n), items.Skip(257).Select(item => item.Value.N));
    }

    [Fact]
    public async Task A_latest_only_subscription_skips_to_its_newest_value_without_a_gap()
    {
        var subscription = Streams.LevelAsync(1000, Token).GetAsyncEnumerator(Token);
        await using var _ = subscription;

        Assert.True(await subscription.MoveNextAsync());
        var first = subscription.Current;
        await Marks.ReachedAsync("level offered").WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(await subscription.MoveNextAsync());

        Assert.Equal(0, first.Value.N);
        Assert.Equal(999, subscription.Current.Value.N);
    }

    [Fact]
    public async Task An_automatic_subscription_reconnects_when_the_channel_comes_back()
    {
        var subscription = Streams.TicksAsync(0, Token).GetAsyncEnumerator(Token);
        await using var _ = subscription;
        Assert.True(await subscription.MoveNextAsync());
        Assert.True(subscription.Current.IsValue);

        _app.DropChannel();
        var events = new List<SparSubscriptionEvent>();
        while (await subscription.MoveNextAsync() && subscription.Current.Event is { } dropped)
        {
            events.Add(dropped);
            if (dropped is SparSubscriptionEvent.Reconnecting)
            {
                _app.RestoreChannel();
            }
        }

        Assert.Equal([new SparSubscriptionEvent.Reconnecting(1), new SparSubscriptionEvent.Reset(), new SparSubscriptionEvent.Resumed()], events);
        Assert.True(subscription.Current.IsValue);
    }

    [Fact]
    public async Task A_manual_subscription_ends_as_lost_when_the_channel_drops()
    {
        var subscription = Streams.DrainAsync(Token).GetAsyncEnumerator(Token);
        await using var _ = subscription;
        Assert.True(await subscription.MoveNextAsync());

        _app.DropChannel();
        var lost = await Assert.ThrowsAsync<SparCallFailedException>(async () => await subscription.MoveNextAsync());

        Assert.Equal(("transport", "lost", "unknown"), (lost.Failure.Kind, lost.Failure.Code, lost.Failure.Execution));
        _app.RestoreChannel();
    }
}
