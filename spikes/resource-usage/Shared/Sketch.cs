using System.Diagnostics;
using System.Threading.Channels;

namespace Spar.ResourceUsage;

// SKETCH ONLY. Not a proposed public API: the shape is to be designed with the user. It exists to measure what a
// latest-value-wins subscription costs per tick (time, allocations) and to show that nothing in it needs the UI thread.

/// <summary>One memory reading in bytes. <c>Private</c> is the cross-OS measure; the rest is detail.</summary>
internal readonly record struct MemoryReading(long Private, long Resident, int Processes, long TimestampTicks);

/// <summary>One folder reading in bytes.</summary>
internal readonly record struct FolderReading(long Logical, long Allocated, long Files, long TimestampTicks);

/// <summary>
/// Samples one measure on a timer while it has a reader, and keeps only the newest value (capacity 1, drop oldest).
/// A value is written only when it differs from the last written one by more than the threshold.
/// </summary>
internal sealed class LatestValueSampler<T>(Func<CancellationToken, ValueTask<T>> read, Func<T, T, bool> changed, TimeSpan interval)
    where T : struct
{
    private readonly Channel<T> _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true,
    });

    public long Ticks { get; private set; }

    public long Written { get; private set; }

    public long Dropped { get; private set; }

    /// <summary>Bytes allocated by the read and the write of each tick after the first (same-thread part only).</summary>
    public long TickAllocated { get; private set; }

    public ChannelReader<T> Reader => _channel.Reader;

    /// <summary>Runs on the thread pool until cancelled. Never touches a UI thread.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        T? last = null;
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                Ticks++;
                var before = GC.GetAllocatedBytesForCurrentThread();
                var pending = read(cancellationToken);
                var value = pending.IsCompletedSuccessfully ? pending.Result : await pending.ConfigureAwait(false);
                if (last is { } previous && !changed(previous, value))
                {
                    if (Ticks > 1)
                    {
                        TickAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
                    }

                    continue;
                }

                if (_channel.Reader.Count > 0)
                {
                    Dropped++;
                }

                _channel.Writer.TryWrite(value);
                Written++;
                last = value;
                if (Ticks > 1)
                {
                    TickAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static long TickAllocatedPer(LatestValueSampler<T> sampler) => sampler.Ticks > 1 ? sampler.TickAllocated / (sampler.Ticks - 1) : 0;

    /// <summary>A relative threshold on one field: changed when |a-b| &gt; max(minimum, fraction * a).</summary>
    public static bool Beyond(long a, long b, double fraction, long minimum) => Math.Abs(a - b) > Math.Max(minimum, (long)(Math.Abs(a) * fraction));

    /// <summary>Drives the sampler for <paramref name="duration"/> with a slow reader and logs what it cost.</summary>
    public static async Task MeasureAsync(string label, LatestValueSampler<T> sampler, TimeSpan duration, TimeSpan readerDelay)
    {
        using var cancel = new CancellationTokenSource(duration);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        var run = Task.Run(() => sampler.RunAsync(cancel.Token));

        // Steady state: allocations between 1 s in and the end, per tick (process-wide, so other threads count too).
        var warm = Task.Run(async () =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            return (sampler.Ticks, GC.GetTotalAllocatedBytes(true));
        });
        long received = 0;
        try
        {
            while (await sampler.Reader.WaitToReadAsync(cancel.Token).ConfigureAwait(false))
            {
                while (sampler.Reader.TryRead(out _))
                {
                    received++;
                }

                await Task.Delay(readerDelay, cancel.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }

        await run.ConfigureAwait(false);
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var (warmTicks, warmAllocated) = await warm.ConfigureAwait(false);
        var steady = (GC.GetTotalAllocatedBytes(true) - warmAllocated) / Math.Max(1, sampler.Ticks - warmTicks);
        Log.Line("SAMPLER", $"label={label} seconds={Log.Ms(started) / 1000:F1} ticks={sampler.Ticks} written={sampler.Written} dropped={sampler.Dropped} " +
            $"received={received} allocatedB={allocated} steadyAllocatedBPerTick={steady} readWriteAllocatedBPerTick={TickAllocatedPer(sampler)}");
    }
}
