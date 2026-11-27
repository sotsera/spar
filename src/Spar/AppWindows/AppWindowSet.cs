namespace Spar.AppWindows;

/// <summary>A window the app opened, or is opening, of one of its kinds.</summary>
internal sealed class OpenWindow
{
    /// <summary>The window's id, unique in the app's run: the kind's name and a number (<c>settings-1</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The kind's options from <c>Program.cs</c>.</summary>
    public required WindowOptions Options { get; init; }

    /// <summary>The JSON its opener gave, which its page reads with <c>windows.params()</c>; empty for none.</summary>
    public ReadOnlyMemory<byte> Params { get; init; }

    /// <summary>The id of the window whose page opened it.</summary>
    public required string OpenerId { get; init; }

    public string Kind => Options.Kind!;
}

/// <summary>The host's side of opening windows; both are called off the UI thread and post their work to it.</summary>
internal interface IAppWindowHost
{
    /// <summary>
    /// Opens <paramref name="window"/> and completes once it is shown; the host calls <see cref="AppWindowSet.Closed"/>
    /// when it closes. A failure (an exception) means no window was left open.
    /// </summary>
    Task OpenAsync(OpenWindow window, CancellationToken cancellationToken);

    /// <summary>Brings an open window to the front, restoring it when it is minimized.</summary>
    void Focus(OpenWindow window);
}

/// <summary>How an open request ended.</summary>
internal enum OpenOutcome
{
    Opened,
    Focused,
    UnknownKind,
    NotOpened,
}

internal sealed record OpenResult(OpenOutcome Outcome, string? Reason = null);

/// <summary>
/// The windows of the app's kinds that are open (decision "M3d's decide-first questions", item 3): ids, the
/// single-instance rule, and the params each window's page reads. One per app; the main window is the host's own and
/// is not listed here. The host's UI thread is never blocked: opening awaits the host, which posts to it.
/// </summary>
internal sealed class AppWindowSet(SparBuilder builder, IAppWindowHost host)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OpenWindow> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private bool _closing;

    /// <summary>The windows open now, in no particular order.</summary>
    public IReadOnlyList<OpenWindow> Open
    {
        get
        {
            lock (_gate)
            {
                return [.. _open.Values];
            }
        }
    }

    /// <summary>
    /// Opens a window of <paramref name="kind"/> for the page of window <paramref name="openerId"/>; a single-instance
    /// kind that is open is focused instead.
    /// </summary>
    public async Task<OpenResult> OpenAsync(string kind, ReadOnlyMemory<byte> parameters, string openerId, CancellationToken cancellationToken)
    {
        if (!builder.WindowKinds.TryGetValue(kind, out var options))
        {
            return new OpenResult(OpenOutcome.UnknownKind);
        }

        OpenWindow? existing = null;
        OpenWindow window;
        lock (_gate)
        {
            if (_closing)
            {
                return new OpenResult(OpenOutcome.NotOpened, "The app is closing.");
            }

            if (options.IsSingleInstance)
            {
                existing = _open.Values.FirstOrDefault(open => open.Kind == kind);
            }

            if (existing is not null)
            {
                window = existing;
            }
            else
            {
                var number = _counts.GetValueOrDefault(kind) + 1;
                _counts[kind] = number;
                window = new OpenWindow { Id = $"{kind}-{number}", Options = options, Params = parameters, OpenerId = openerId };
                _open.Add(window.Id, window);
            }
        }

        if (existing is not null)
        {
            host.Focus(existing);
            return new OpenResult(OpenOutcome.Focused);
        }

        try
        {
            await host.OpenAsync(window, cancellationToken);
            return new OpenResult(OpenOutcome.Opened);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Closed(window.Id);
            return new OpenResult(OpenOutcome.NotOpened, error.Message);
        }
        catch (OperationCanceledException)
        {
            Closed(window.Id);
            throw;
        }
    }

    /// <summary>A window closed (the host, on any thread); closing one that is not listed does nothing.</summary>
    public void Closed(string id)
    {
        lock (_gate)
        {
            _open.Remove(id);
        }
    }

    /// <summary>The app is closing: no window opens after this.</summary>
    public void Closing()
    {
        lock (_gate)
        {
            _closing = true;
        }
    }
}
