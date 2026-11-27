namespace Spar.AppWindows;

/// <summary>A window the app opened, or is opening, of one of its kinds.</summary>
internal sealed class OpenWindow
{
    private readonly TaskCompletionSource<ModalResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The window's id, unique in the app's run: the kind's name and a number (<c>settings-1</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The kind's options from <c>Program.cs</c>.</summary>
    public required WindowOptions Options { get; init; }

    /// <summary>The JSON its opener gave, which its page reads with <c>windows.params()</c>; empty for none.</summary>
    public ReadOnlyMemory<byte> Params { get; init; }

    /// <summary>The id of the window whose page opened it: a modal's owner, which takes no input while it is open.</summary>
    public required string OpenerId { get; init; }

    public string Kind => Options.Kind!;

    public bool IsModal => Options.IsModal;

    /// <summary>A modal's result: <c>closeWith</c>'s value, or dismissed once it closed without one.</summary>
    public Task<ModalResult> Result => _result.Task;

    /// <summary>Whether a page still waits for the modal's result; false once its call ended (the opener's document went away).</summary>
    public bool Awaited { get; set; }

    /// <summary>Sets the result once; false when the modal already has one.</summary>
    public bool TrySetResult(ModalResult result) => _result.TrySetResult(result);
}

/// <summary>The host's side of opening windows; every member is called off the UI thread and posts its work to it.</summary>
internal interface IAppWindowHost
{
    /// <summary>
    /// Opens <paramref name="window"/> and completes once it is shown; the host calls <see cref="AppWindowSet.Closed"/>
    /// when it closes. A modal is owned by its opener's window, which takes no input while it is open. A failure (an
    /// exception) means no window was left open.
    /// </summary>
    Task OpenAsync(OpenWindow window, CancellationToken cancellationToken);

    /// <summary>Brings an open window to the front, restoring it when it is minimized.</summary>
    void Focus(OpenWindow window);

    /// <summary>Closes an open window at once, without asking its guards: its own page asked (<c>windows.closeWith</c>).</summary>
    void Close(OpenWindow window);
}

/// <summary>How an open request ended.</summary>
internal enum OpenOutcome
{
    Opened,
    Focused,
    UnknownKind,

    /// <summary><c>open</c> of a modal kind, or <c>showModal</c> of one that is not.</summary>
    WrongKind,
    NotOpened,
}

internal sealed record OpenResult(OpenOutcome Outcome, string? Reason = null);

/// <summary>How a modal ended for its opener.</summary>
internal enum ModalOutcome
{
    /// <summary>The modal's page closed it with <c>closeWith(value)</c>.</summary>
    Value,

    /// <summary>It closed any other way: its close button, its opener's close, the app's.</summary>
    Dismissed,
}

/// <summary>A modal's result: <see cref="Value"/> is <c>closeWith</c>'s JSON (<c>null</c> for none).</summary>
internal sealed record ModalResult(ModalOutcome Outcome, ReadOnlyMemory<byte> Value = default)
{
    public static ModalResult Dismissed { get; } = new(ModalOutcome.Dismissed);
}

/// <summary>
/// The windows of the app's kinds that are open (decision "M3d's decide-first questions", items 3 and 4): ids, the
/// single-instance rule, the params each window's page reads, and each modal's result for its opener. One per app; the
/// main window is the host's own and is not listed here. The host's UI thread is never blocked: opening awaits the host,
/// which posts to it.
/// </summary>
internal sealed class AppWindowSet(SparBuilder builder, IAppWindowHost host, Action<string>? warn = null)
{
    private readonly Lock _gate = new();
    private readonly OrderedDictionary<string, OpenWindow> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private bool _closing;

    /// <summary>The windows open now, in the order they opened.</summary>
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

    /// <summary>The open window with <paramref name="id"/>, or null.</summary>
    public OpenWindow? Find(string id)
    {
        lock (_gate)
        {
            return _open.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Opens a window of <paramref name="kind"/> for the page of window <paramref name="openerId"/>; a single-instance
    /// kind that is open is focused instead. A modal kind is refused: it is shown with <see cref="ShowModalAsync"/>.
    /// </summary>
    public async Task<OpenResult> OpenAsync(string kind, ReadOnlyMemory<byte> parameters, string openerId, CancellationToken cancellationToken)
    {
        var (result, _) = await StartAsync(kind, parameters, openerId, modal: false, cancellationToken);
        return result;
    }

    /// <summary>
    /// Shows a modal of <paramref name="kind"/>, owned by window <paramref name="openerId"/>, and waits until it closes:
    /// <c>closeWith</c>'s value, or dismissed. When the wait ends first (<paramref name="cancellationToken"/>: the
    /// opener's document went away, or its page stopped waiting), the modal stays open and its result is dropped and
    /// logged when it comes.
    /// </summary>
    public async Task<(OpenResult Opened, ModalResult? Result)> ShowModalAsync(
        string kind, ReadOnlyMemory<byte> parameters, string openerId, CancellationToken cancellationToken)
    {
        var (opened, window) = await StartAsync(kind, parameters, openerId, modal: true, cancellationToken);
        if (opened.Outcome != OpenOutcome.Opened || window is null)
        {
            return (opened, null);
        }

        try
        {
            return (opened, await window.Result.WaitAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            window.Awaited = false;
            throw;
        }
    }

    /// <summary>
    /// The modal <paramref name="id"/>'s page closed it with <paramref name="value"/> (<c>windows.closeWith</c>): the
    /// value goes to its opener, and the host closes it without asking its guards. A window that is not a modal closes
    /// the same way, and the value goes nowhere. False when no window of the app's kinds has that id (the main window).
    /// </summary>
    public bool CloseWith(string id, ReadOnlyMemory<byte> value)
    {
        var window = Find(id);
        if (window is null)
        {
            return false;
        }

        if (window.IsModal)
        {
            window.TrySetResult(new ModalResult(ModalOutcome.Value, value));
        }

        host.Close(window);
        return true;
    }

    /// <summary>
    /// A window closed (the host, on any thread): a modal without a result is dismissed, and a result nobody waits for
    /// any more is logged. Closing one that is not listed does nothing.
    /// </summary>
    public void Closed(string id)
    {
        OpenWindow? window;
        lock (_gate)
        {
            if (!_open.Remove(id, out window))
            {
                return;
            }
        }

        if (!window.IsModal)
        {
            return;
        }

        window.TrySetResult(ModalResult.Dismissed);
        if (!window.Awaited)
        {
            var result = window.Result.Result;
            var what = result.Outcome == ModalOutcome.Value ? "its closeWith value" : "dismissed";
            warn?.Invoke($"The modal {id} closed ({what}) after its opener {window.OpenerId} stopped waiting: its document went away or its call was aborted. The result is dropped.");
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

    private async Task<(OpenResult Result, OpenWindow? Window)> StartAsync(
        string kind, ReadOnlyMemory<byte> parameters, string openerId, bool modal, CancellationToken cancellationToken)
    {
        if (!builder.WindowKinds.TryGetValue(kind, out var options))
        {
            return (new OpenResult(OpenOutcome.UnknownKind), null);
        }

        if (options.IsModal != modal)
        {
            var reason = modal
                ? $"The window kind '{kind}' is not a modal: open it with windows.open, or declare it with Modal()."
                : $"The window kind '{kind}' is a modal: show it with windows.showModal, which waits for its result.";
            return (new OpenResult(OpenOutcome.WrongKind, reason), null);
        }

        OpenWindow? existing = null;
        OpenWindow window;
        lock (_gate)
        {
            if (_closing)
            {
                return (new OpenResult(OpenOutcome.NotOpened, "The app is closing."), null);
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
                window = new OpenWindow { Id = $"{kind}-{number}", Options = options, Params = parameters, OpenerId = openerId, Awaited = modal };
                _open.Add(window.Id, window);
            }
        }

        if (existing is not null)
        {
            host.Focus(existing);

            // A modal belongs to the window that showed it: another page gets no share of its result.
            return existing.IsModal
                ? (new OpenResult(OpenOutcome.NotOpened, $"The {kind} window is already open ({existing.Id}); it was brought to the front."), null)
                : (new OpenResult(OpenOutcome.Focused), existing);
        }

        try
        {
            await host.OpenAsync(window, cancellationToken);
            return (new OpenResult(OpenOutcome.Opened), window);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            window.Awaited = false;
            Closed(window.Id);
            return (new OpenResult(OpenOutcome.NotOpened, error.Message), null);
        }
        catch (OperationCanceledException)
        {
            window.Awaited = false;
            Closed(window.Id);
            throw;
        }
    }
}
