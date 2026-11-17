using System.Collections.Concurrent;
using Spar;

namespace SqlTool.Support;

/// <summary>The page's hangs in this run, for the status page and support reports; Program.cs records each change.</summary>
public sealed class HangLog
{
    private const int Kept = 50;

    private readonly ConcurrentQueue<PageHang> _changes = new();

    /// <summary>One change: unresponsive, recovered, reloaded or closed anyway. Spar's dialog has already asked the user.</summary>
    public void Record(PageHang hang)
    {
        _changes.Enqueue(hang);
        while (_changes.Count > Kept && _changes.TryDequeue(out _))
        {
        }
    }

    /// <summary>How often the page stopped responding, and the longest it went without answering.</summary>
    public (int Hangs, TimeSpan Longest) Summary()
    {
        var hangs = _changes.Count(change => change.State == PageHangState.Unresponsive);
        var longest = _changes.Select(change => change.Duration).DefaultIfEmpty(TimeSpan.Zero).Max();
        return (hangs, longest);
    }
}
