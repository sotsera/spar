using System.Runtime.CompilerServices;
using Spar;

namespace WireShapes;

public sealed record Row(int N, string Text);

public sealed record Tick(long N);

public closed record RowsError
{
    /// <summary>The stream stopped at the row it could not read.</summary>
    public sealed record Unreadable(int At) : RowsError;
}

/// <summary>
/// Streams and subscriptions, for the typed test clients, the browser tests and the bridge's budgets: rows as fast as
/// they are taken, a stream that ends with a declared error, and subscriptions that keep a buffer or only their newest
/// value, as fast as possible or paced.
/// </summary>
[SparContract]
public sealed class StreamsService(StreamMarks marks)
{
    /// <summary>The first <paramref name="count"/> rows, as fast as the page takes them.</summary>
    [SparCommand]
    public async IAsyncEnumerable<Row> RowsAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var n = 0; n < count; n++)
        {
            if (n % 1024 == 0)
            {
                // A real reader awaits between pages of rows; the rest come synchronously.
                await Task.Yield();
            }

            yield return new Row(n, $"row {n}");
        }
    }

    /// <summary>Rows until the one at <paramref name="failAt"/>, which ends the stream with a declared error.</summary>
    [SparCommand]
    public async IAsyncEnumerable<Result<Row, RowsError>> ReadAsync(int failAt, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var n = 0; ; n++)
        {
            await Task.Yield();
            if (n == failAt)
            {
                yield return new RowsError.Unreadable(n);
                yield break;
            }

            yield return new Row(n, $"row {n}");
        }
    }

    /// <summary>
    /// <paramref name="count"/> ticks at once, then after a pause one each 10 ms until it is cancelled; it marks when
    /// the first ones were offered, so a test knows the buffer is full.
    /// </summary>
    [SparCommand]
    [SparSubscription(Capacity = 8)]
    public async IAsyncEnumerable<Tick> TicksAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long n = 0;
        for (; n < count; n++)
        {
            yield return new Tick(n);
        }

        marks.Note("ticks offered");

        // A pause before the live ticks, so a test reads the first ones before a new tick moves the buffer on.
        await Task.Delay(250, cancellationToken);
        while (true)
        {
            await Task.Delay(10, cancellationToken);
            yield return new Tick(n++);
        }
    }

    /// <summary>The newest of <paramref name="count"/> values offered at once, then it waits.</summary>
    [SparCommand]
    [SparSubscription(LatestOnly = true)]
    public async IAsyncEnumerable<Tick> LevelAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (long n = 0; n < count; n++)
        {
            yield return new Tick(n);
        }

        marks.Note("level offered");
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    /// <summary>A subscription with side effects: it never subscribes again by itself.</summary>
    [SparCommand]
    [SparSubscription(Resume = SubscriptionResume.Manual)]
    public async IAsyncEnumerable<Tick> DrainAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new Tick(0);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    /// <summary>
    /// <paramref name="perSecond"/> ticks a second for a second, as fast as a producer that never waits can offer them,
    /// for the push budget; the buffer is large enough that none is dropped when the page keeps up.
    /// </summary>
    [SparCommand]
    [SparSubscription(Capacity = 32768)]
    public async IAsyncEnumerable<Tick> BurstAsync(int perSecond, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (long n = 0; n < perSecond; n++)
        {
            // Paced in 50 slices a second, so the page sees a steady rate rather than one burst.
            var due = TimeSpan.FromSeconds((double)n / perSecond);
            if (due > started.Elapsed + TimeSpan.FromMilliseconds(20))
            {
                await Task.Delay(due - started.Elapsed, cancellationToken);
            }

            yield return new Tick(n);
        }

        marks.Note("burst done");
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

/// <summary>Points a producer reached, so a test acts only once a buffer is full; one per app instance.</summary>
public sealed class StreamMarks
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource> _marks = new(StringComparer.Ordinal);

    public void Note(string mark) => Mark(mark).TrySetResult();

    public Task ReachedAsync(string mark) => Mark(mark).Task;

    /// <summary>Forgets a mark, so a producer that runs again (a resumed subscription) can be waited for once more.</summary>
    public void Forget(string mark) => _marks.TryRemove(mark, out _);

    private TaskCompletionSource Mark(string mark) =>
        _marks.GetOrAdd(mark, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
