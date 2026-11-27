namespace Spar.Files;

/// <summary>
/// The files of drops a host saw itself (decision "The drop design, from the file-drop spike", item 3, Linux): the
/// host reads each drag's files when it enters the view; a trusted <c>drop</c> event, which only the bridge relay's
/// world reports, turns that drag into a drop the page may claim, once. A page's claim with no such drop gets nothing,
/// so a page cannot turn a drag the user only passed over the window into grants.
/// </summary>
internal sealed class DropInbox
{
    /// <summary>How long a claim waits for the drag's files, which the host reads as the drag enters.</summary>
    private static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private Task<IReadOnlyList<string>>? _dragging;
    private Task<IReadOnlyList<string>>? _dropped;

    /// <summary>A drag entered the view; its files are being read (the host's loop thread).</summary>
    public void DragEntered(Task<IReadOnlyList<string>> files)
    {
        lock (_gate)
        {
            _dragging = files;
        }
    }

    /// <summary>The relay saw a trusted drop: the current drag's files wait for the page's claim.</summary>
    public void Dropped()
    {
        lock (_gate)
        {
            _dropped = _dragging;
            _dragging = null;
        }
    }

    /// <summary>The files of the last trusted drop, once; null when there is none to claim.</summary>
    public async Task<IReadOnlyList<string>?> ClaimAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<string>>? files;
        lock (_gate)
        {
            files = _dropped;
            _dropped = null;
        }

        if (files is null)
        {
            return null;
        }

        try
        {
            return await files.WaitAsync(ReadLimit, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The read timed out or failed (the drag's source went away): there is nothing to grant.
            return null;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _dragging = null;
            _dropped = null;
        }
    }
}
