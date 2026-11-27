using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Notebook.Pages;

/// <summary>What happened to a page.</summary>
public enum PageChangeKind
{
    Created,
    Saved,
    Deleted,
}

/// <summary>A page created, saved or deleted, with the revision it has now (0 once deleted).</summary>
public sealed record PageChange(string Id, PageChangeKind Kind, long Revision);

/// <summary>
/// Tells every open window what changed in the notebook, whoever changed it: another window, a browser tab while
/// developing, or a test. Each watcher has its own queue, emptied as fast as Spar takes the values, so a slow page never
/// slows the save that announced the change; Spar's subscription buffer bounds what waits for the page.
/// </summary>
public sealed class PageChanges
{
    private readonly Lock _gate = new();
    private readonly List<Channel<PageChange>> _watchers = [];

    public void Publish(PageChange change)
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryWrite(change);
            }
        }
    }

    /// <summary>The changes from now on, until <paramref name="cancellationToken"/> ends the watch.</summary>
    public async IAsyncEnumerable<PageChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var watcher = Channel.CreateUnbounded<PageChange>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            _watchers.Add(watcher);
        }

        try
        {
            await foreach (var change in watcher.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchers.Remove(watcher);
            }
        }
    }
}
