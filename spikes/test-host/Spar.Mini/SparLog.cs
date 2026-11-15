using System.Collections.Concurrent;

namespace Spar;

/// <summary>The app's log. Spar code writes only here, never to <see cref="Console"/>, so apps in one process stay apart.</summary>
public interface ISparLog
{
    void Write(string category, string message, Exception? exception = null);
}

internal sealed class FileSparLog(string folder) : ISparLog
{
    private readonly Lock _gate = new();

    public void Write(string category, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "app.log"), $"{DateTimeOffset.UtcNow:O} {category} {message} {exception}\n");
        }
    }
}

/// <summary>A test host's log: kept in memory, one per app instance.</summary>
public sealed class MemorySparLog : ISparLog
{
    private readonly ConcurrentQueue<SparLogEntry> _entries = new();

    public IReadOnlyList<SparLogEntry> Entries => [.. _entries];

    public void Write(string category, string message, Exception? exception = null) =>
        _entries.Enqueue(new(category, message, exception));
}

public sealed record SparLogEntry(string Category, string Message, Exception? Exception);
