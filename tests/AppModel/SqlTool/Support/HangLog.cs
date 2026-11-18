using System.Collections.Concurrent;
using System.Globalization;
using Spar;

namespace SqlTool.Support;

/// <summary>
/// The page's hangs, for the status page and support reports: kept in memory for this run, and appended to
/// <c>hangs.txt</c> in the logs folder for the support team. Program.cs records each change.
/// </summary>
public sealed class HangLog
{
    private const int Kept = 50;

    private readonly ConcurrentQueue<PageHang> _changes = new();
    private readonly string _filePath;

    /// <summary>A record in <paramref name="logsFolder"/>, the logs folder Program.cs resolved.</summary>
    public HangLog(string logsFolder)
    {
        _filePath = Path.Combine(logsFolder, "hangs.txt");
    }

    /// <summary>
    /// One change: unresponsive, recovered, reloaded or closed anyway. Spar's dialog has already asked the user. Spar
    /// awaits it before the next change, and cancels the token when the app exits and stops waiting.
    /// </summary>
    public async ValueTask RecordAsync(PageHang hang, CancellationToken cancellationToken)
    {
        _changes.Enqueue(hang);
        while (_changes.Count > Kept && _changes.TryDequeue(out _))
        {
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O} {hang.Window} {hang.State} after {hang.Duration.TotalSeconds:0.0} s{Environment.NewLine}");
        await File.AppendAllTextAsync(_filePath, line, cancellationToken);
    }

    /// <summary>How often the page stopped responding in this run, and the longest it went without answering.</summary>
    public (int Hangs, TimeSpan Longest) Summary()
    {
        var hangs = _changes.Count(change => change.State == PageHangState.Unresponsive);
        var longest = _changes.Select(change => change.Duration).DefaultIfEmpty(TimeSpan.Zero).Max();
        return (hangs, longest);
    }
}
