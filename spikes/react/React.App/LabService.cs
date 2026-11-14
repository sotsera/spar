using System.Diagnostics;
using System.Runtime.CompilerServices;
using Spike;

namespace React.App;

/// <summary>A result-grid row: ten columns, about 200 bytes of JSON.</summary>
public sealed record Row(
    int Id,
    string Name,
    string Email,
    double Amount,
    int Quantity,
    bool Active,
    string CreatedAt,
    string Category,
    double Score,
    string? Note);

public sealed record RowQuery(int Count, int? DelayEveryRows, int? DelayMs);

/// <summary>Work that increments a shared counter every tick until it ends or is cancelled.</summary>
public sealed record WorkRequest(int DurationMs, int TickMs, bool HonorCancellation);

public sealed record WatchRequest(int RatePerSecond, int? Seconds);

public sealed record MonitorEvent(long Sequence, double SentAtMs, double Value, string Source);

public sealed record LabStats(long Counter, long RowsProduced, int ActiveWatches, long WorkStarted, long WorkStopped);

public sealed record MemoryReport(long GcHeapBytes, long WorkingSetBytes, long PeakGcHeapBytes, long PeakWorkingSetBytes, double CpuMs);

/// <summary>
/// Spike lab contract for streaming, cancellation and push measurements. A singleton, so its counters are visible to
/// later calls: the page checks that cancelled work really stopped by reading the counter twice.
/// </summary>
[SparContract("lab")]
public sealed class LabService
{
    private static readonly string[] Categories = ["alpha", "beta", "gamma", "delta", "epsilon"];

    private long _counter;
    private long _rowsProduced;
    private int _activeWatches;
    private long _workStarted;
    private long _workStopped;
    private long _peakHeap;
    private long _peakWorkingSet;
    private CancellationTokenSource? _sampler;
    private TimeSpan _cpuAtStart;

    /// <summary>Streams synthetic rows with back-pressure: the producer advances only as fast as the host pulls.</summary>
    [SparCommand]
    public async IAsyncEnumerable<Row> RowsAsync(RowQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _rowsProduced, 0);
        for (var i = 0; i < query.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.DelayEveryRows is > 0 && i > 0 && i % query.DelayEveryRows.Value == 0)
            {
                await Task.Delay(query.DelayMs ?? 1, cancellationToken);
            }

            Interlocked.Increment(ref _rowsProduced);
            yield return new Row(
                i,
                "Customer " + i,
                $"customer{i}@example.com",
                i * 1.25,
                i % 100,
                i % 3 != 0,
                "2026-11-13T10:00:00Z",
                Categories[i % Categories.Length],
                (i % 1000) / 10.0,
                i % 4 == 0 ? null : "Row note " + i);
        }
    }

    /// <summary>Unary work: ticks the counter until it is done or cancelled (or, with HonorCancellation=false, ignores cancellation).</summary>
    [SparCommand]
    public async Task<long> WorkAsync(WorkRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _workStarted);
        try
        {
            var token = request.HonorCancellation ? cancellationToken : CancellationToken.None;
            var started = Stopwatch.GetTimestamp();
            long ticks = 0;
            while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < request.DurationMs)
            {
                Interlocked.Increment(ref _counter);
                ticks++;
                await Task.Delay(request.TickMs, token);
            }

            return ticks;
        }
        finally
        {
            Interlocked.Increment(ref _workStopped);
        }
    }

    /// <summary>Streaming work: yields the counter on every tick.</summary>
    [SparCommand]
    public async IAsyncEnumerable<long> TicksAsync(WorkRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _workStarted);
        try
        {
            var token = request.HonorCancellation ? cancellationToken : CancellationToken.None;
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < request.DurationMs)
            {
                yield return Interlocked.Increment(ref _counter);
                await Task.Delay(request.TickMs, token);
            }
        }
        finally
        {
            Interlocked.Increment(ref _workStopped);
        }
    }

    /// <summary>Live monitoring: events at a fixed rate, stamped with the wall clock when produced.</summary>
    [SparCommand]
    [SparSubscription(1000)]
    public async IAsyncEnumerable<MonitorEvent> WatchAsync(WatchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeWatches);
        try
        {
            var started = Stopwatch.GetTimestamp();
            long sequence = 0;
            while (request.Seconds is not > 0 || Stopwatch.GetElapsedTime(started).TotalSeconds < request.Seconds)
            {
                // Timers tick at about 1-16 ms here, so emit every event that is due since the start.
                var due = (long)(Stopwatch.GetElapsedTime(started).TotalSeconds * request.RatePerSecond);
                while (sequence < due)
                {
                    var now = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
                    yield return new MonitorEvent(sequence, now, Math.Sin(sequence / 100.0), "server-1");
                    sequence++;
                }

                await Task.Delay(1, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeWatches);
        }
    }

    [SparCommand]
    public Task<LabStats> StatsAsync() => Task.FromResult(new LabStats(
        Interlocked.Read(ref _counter),
        Interlocked.Read(ref _rowsProduced),
        Volatile.Read(ref _activeWatches),
        Interlocked.Read(ref _workStarted),
        Interlocked.Read(ref _workStopped)));

    /// <summary>Starts sampling backend memory every 5 ms (peaks), until <see cref="MemoryStopAsync"/>.</summary>
    [SparCommand]
    public Task<MemoryReport> MemoryStartAsync()
    {
        _sampler?.Cancel();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var sampler = _sampler = new CancellationTokenSource();
        _peakHeap = GC.GetTotalMemory(false);
        _peakWorkingSet = Environment.WorkingSet;
        _cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
        _ = Task.Run(async () =>
        {
            while (!sampler.IsCancellationRequested)
            {
                _peakHeap = Math.Max(_peakHeap, GC.GetTotalMemory(false));
                _peakWorkingSet = Math.Max(_peakWorkingSet, Environment.WorkingSet);
                await Task.Delay(5, CancellationToken.None);
            }
        }, CancellationToken.None);
        return Task.FromResult(Report());
    }

    [SparCommand]
    public Task<MemoryReport> MemoryStopAsync()
    {
        _sampler?.Cancel();
        return Task.FromResult(Report());
    }

    private MemoryReport Report()
    {
        using var process = Process.GetCurrentProcess();
        return new MemoryReport(
            GC.GetTotalMemory(false),
            Environment.WorkingSet,
            _peakHeap,
            _peakWorkingSet,
            (process.TotalProcessorTime - _cpuAtStart).TotalMilliseconds);
    }
}
