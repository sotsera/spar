using System.Collections.Concurrent;
using System.Text;
using Spar.Runtime;

namespace Spar.Tests.Serial;

/// <summary>Hand-written commands of one serial group, standing in for what the generator writes.</summary>
internal static class TestSerial
{
    /// <summary>The names of the calls that started running, in order.</summary>
    public static ConcurrentQueue<string> Started { get; } = new();

    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new(StringComparer.Ordinal);

    public static IEnumerable<SparCommandEntry> Entries =>
    [
        new("serial", "hold", 0, HoldAsync) { SerialGroup = "g" },
        new("serial", "limited", 1, HoldAsync) { SerialGroup = "g" },
    ];

    /// <summary>Lets the call named <paramref name="name"/> finish.</summary>
    public static void Release(string name) => Gate(name).TrySetResult();

    private static TaskCompletionSource Gate(string name) =>
        Gates.GetOrAdd(name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Runs until its name is released, or its token (its time limit included) ends it.</summary>
    private static async ValueTask<SparOutcome> HoldAsync(SparCallContext call)
    {
        var name = Encoding.UTF8.GetString(call.Request.Span).Trim('"');
        Started.Enqueue(name);
        await Gate(name).Task.WaitAsync(call.CancellationToken);
        return SparOutcome.FromValue(call.Request.ToArray());
    }
}
