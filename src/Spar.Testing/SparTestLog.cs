using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Spar.Testing;

/// <summary>One entry of an app instance's log.</summary>
public sealed record SparLogEntry(DateTimeOffset At, LogLevel Level, string Category, EventId EventId, string Message, Exception? Exception)
{
    public override string ToString() =>
        $"{At:HH:mm:ss.fff} {Level} {Category}[{EventId.Id}] {Message}{(Exception is null ? "" : Environment.NewLine + Exception)}";
}

/// <summary>
/// What Spar logs for one app instance in a test (plan section 5: "each app instance has its own log sink"): every
/// level, in order, kept in memory up to <see cref="Capacity"/> entries; past that the oldest are dropped and counted.
/// The app's own logging goes where the app sends it.
/// </summary>
internal sealed class SparTestLog(TimeProvider time) : ILogger
{
    public const int Capacity = 10_000;

    private readonly ConcurrentQueue<SparLogEntry> _entries = new();
    private int _count;
    private int _dropped;

    public IReadOnlyList<SparLogEntry> Entries => [.. _entries];

    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>The category Spar's bridge logs under; the test log holds nothing else today.</summary>
    public string Category { get; init; } = "Spar.Bridge";

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        _entries.Enqueue(new SparLogEntry(time.GetUtcNow(), logLevel, Category, eventId, formatter(state, exception), exception));
        if (Interlocked.Increment(ref _count) > Capacity && _entries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
            Interlocked.Increment(ref _dropped);
        }
    }
}
