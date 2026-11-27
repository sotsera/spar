using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Spar.Runtime;

namespace Spar.Tests.Streams;

/// <summary>Hand-written stream and subscription handlers, standing in for what the generator writes.</summary>
internal static class TestStreams
{
    /// <summary>Marks a handler reached, so a test knows a producer has yielded everything before it acts.</summary>
    public static ConcurrentQueue<string> Reached { get; } = new();

    public static IEnumerable<SparCommandEntry> Entries =>
    [
        new("rows", "count", 0, null) { StreamHandler = CountAsync },
        new("rows", "failAfter", 0, null) { StreamHandler = FailAfterAsync },
        new("rows", "errorAfter", 0, null) { StreamHandler = ErrorAfterAsync },
        new("rows", "forever", 0, null) { StreamHandler = ForeverAsync },
        new("rows", "badItem", 0, null) { StreamHandler = BadItemAsync },
        new("feed", "ticks", 0, null) { StreamHandler = TicksAsync, Capacity = 4 },
        new("feed", "latest", 0, null) { StreamHandler = TicksAsync, Capacity = 1, LatestOnly = true },
    ];

    public static void WriteRow(Utf8JsonWriter writer, int n)
    {
        writer.WriteStartObject();
        writer.WriteNumber("n"u8, n);
        writer.WriteEndObject();
    }

    private static int Count(SparCallContext call) => int.Parse(Encoding.UTF8.GetString(call.Request.Span));

    private static async ValueTask<SparOutcome> CountAsync(SparCallContext call, SparItemSink sink)
    {
        for (var n = 0; n < Count(call); n++)
        {
            await sink.WriteAsync(n, WriteRow, call.CancellationToken);
        }

        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }

    private static async ValueTask<SparOutcome> FailAfterAsync(SparCallContext call, SparItemSink sink)
    {
        for (var n = 0; n < 3; n++)
        {
            await sink.WriteAsync(n, WriteRow, call.CancellationToken);
        }

        throw new IOException("disk full");
    }

    private static async ValueTask<SparOutcome> ErrorAfterAsync(SparCallContext call, SparItemSink sink)
    {
        await sink.WriteAsync(0, WriteRow, call.CancellationToken);
        await sink.WriteAsync(1, WriteRow, call.CancellationToken);
        return sink.End("tooMany", (writer, code) =>
        {
            writer.WriteStartObject();
            writer.WriteString("code"u8, code);
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// A producer whose own code never looks at its token, as an iterator without [EnumeratorCancellation]: only the
    /// sink, which the generated loop hands the call's token, stops it at its next item.
    /// </summary>
    private static async ValueTask<SparOutcome> ForeverAsync(SparCallContext call, SparItemSink sink)
    {
        for (var n = 0; ; n++)
        {
            await Task.Yield();
            await sink.WriteAsync(n, WriteRow, call.CancellationToken);
        }
    }

    private static async ValueTask<SparOutcome> BadItemAsync(SparCallContext call, SparItemSink sink)
    {
        for (var n = 0; n < 5; n++)
        {
            await sink.WriteAsync(n, (writer, value) =>
            {
                if (value == 2)
                {
                    throw new InvalidOperationException("cannot write 2");
                }

                WriteRow(writer, value);
            }, call.CancellationToken);
        }

        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>A subscription: as many values as the request says, at once, then it waits until it is cancelled.</summary>
    private static async ValueTask<SparOutcome> TicksAsync(SparCallContext call, SparItemSink sink)
    {
        for (var n = 0; n < Count(call); n++)
        {
            await sink.WriteAsync(n, WriteRow, call.CancellationToken);
        }

        Reached.Enqueue("ticks");
        await Task.Delay(Timeout.Infinite, call.CancellationToken);
        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }
}
