namespace Spar.Files;

/// <summary>What a file dialog is asked for; the host shows it owned by its window.</summary>
internal enum FileDialogKind
{
    Open,
    OpenMany,
    Save,
    Folder,
}

/// <summary>One dialog request, as the host's native dialog takes it.</summary>
internal sealed record FileDialogRequest(FileDialogKind Kind, string? Title, IReadOnlyList<FileFilter> Filters, string? SuggestedName);

/// <summary>
/// A host's file dialogs for one window: shown owned by it, on its UI thread or its main loop, never blocking it. The
/// paths chosen (one for <see cref="FileDialogKind.Open"/>, <see cref="FileDialogKind.Save"/> and
/// <see cref="FileDialogKind.Folder"/>), or why there are none. The test host registers a fake of its own as a service,
/// which every window then uses instead.
/// </summary>
internal interface INativeFileDialogs
{
    Task<Result<IReadOnlyList<string>, FileDialogError>> ShowAsync(FileDialogRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The window a call came from, as Spar's services see it: its grants, its file dialogs (one open at a time), and the
/// files of a drop the host has seen but the page has not yet claimed. A document belongs to its window, so the grants
/// outlive a reload; the host closes it with the window, which revokes them.
/// </summary>
internal sealed class HostWindow(TimeProvider time, INativeFileDialogs? dialogs, Func<string, bool>? forbidden = null)
{
    private int _dialogOpen;

    /// <summary>The window's grants, none of which reaches a path <paramref name="forbidden"/> names (the browser profile).</summary>
    public GrantTable Grants { get; } = new(time, forbidden);

    /// <summary>The window's dialogs; null for a host that has none (loopback outside the test host).</summary>
    public INativeFileDialogs? Dialogs { get; } = dialogs;

    /// <summary>The drops the host saw on this window (Linux: the drag's files, read by the host itself).</summary>
    public DropInbox Drops { get; } = new();

    /// <summary>Claims the window's one dialog; false while another is open.</summary>
    public bool TryBeginDialog() => Interlocked.CompareExchange(ref _dialogOpen, 1, 0) == 0;

    public void EndDialog() => Volatile.Write(ref _dialogOpen, 0);

    /// <summary>The window's name in hooks and logs: <c>main</c>, or its kind's.</summary>
    public string Name { get; init; } = AppWindows.WindowRoutes.Main;

    /// <summary>The window's own id, unique in the app: <c>main</c>, or the kind's name with a number (<c>settings-1</c>).</summary>
    public string Id { get; init; } = AppWindows.WindowRoutes.Main;

    /// <summary>The JSON its opener gave (<c>windows.open(kind, params)</c>), which the page reads with <c>windows.params()</c>; empty for none.</summary>
    public ReadOnlyMemory<byte> Params { get; init; }

    /// <summary>The window's allow-list; null when it may call every command.</summary>
    public AppWindows.WindowAccess? Access { get; init; }

    /// <summary>The app's windows, which this one's page may open more of; null for a host without windows of its own (loopback).</summary>
    public AppWindows.AppWindowSet? Windows { get; init; }

    /// <summary>The window closed: its grants are revoked and a drop it held is dropped.</summary>
    public void Close()
    {
        Grants.Close();
        Drops.Clear();
    }
}
