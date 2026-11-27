using System.Text.Json;

namespace Spar.AppWindows;

/// <summary>
/// What a host does to one of its windows for <see cref="IAppWindow"/>; every member may be called from any thread and
/// posts its work to the window's UI thread, which it never blocks. A window that is gone ignores them.
/// </summary>
internal interface IWindowControl
{
    void Focus();

    void Minimize();

    void Maximize();

    void Restore();

    void SetSize(int width, int height);

    void SetTitle(string title);

    /// <summary>The window's own close request with <see cref="CloseReason.App"/>; true once it closed.</summary>
    Task<bool> CloseAsync();
}

/// <summary>
/// An app window as <see cref="IAppWindow"/>: its id and kind, and its host's <see cref="IWindowControl"/>, which the
/// host gives it once it made the native window. Before then, and once the window closed, its members do nothing.
/// </summary>
internal sealed class AppWindowHandle(string id, string kind) : IAppWindow
{
    private volatile IWindowControl? _control;
    private volatile bool _closed;

    public string Id { get; } = id;

    public string Kind { get; } = kind;

    public bool IsOpen => _control is not null && !_closed;

    public void Attach(IWindowControl control) => _control = control;

    public void MarkClosed() => _closed = true;

    public void Focus() => Live()?.Focus();

    public void Minimize() => Live()?.Minimize();

    public void Maximize() => Live()?.Maximize();

    public void Restore() => Live()?.Restore();

    public void SetSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Live()?.SetSize(width, height);
    }

    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Live()?.SetTitle(title);
    }

    public Task<bool> CloseAsync() => Live()?.CloseAsync() ?? Task.FromResult(true);

    public override string ToString() => Id;

    private IWindowControl? Live() => _closed ? null : _control;
}

/// <summary>
/// <see cref="IWindows"/>, the app's windows for its C# code (decision "M3d's decide-first questions", item 8): a
/// singleton of the app's services, which the host connects to its <see cref="AppWindowSet"/> once the services exist.
/// Unconnected (the test host's command surface, a host without windows), it has no main window and opens none.
/// </summary>
internal sealed class SparWindows : IWindows
{
    private AppWindowSet? _set;

    public IAppWindow Main => _set?.Main ?? throw new InvalidOperationException(
        "The app's window does not exist here: a test without the Window surface, or a host that opens none, has no window.");

    public IReadOnlyList<IAppWindow> All
    {
        get
        {
            if (_set is not { } set)
            {
                return [];
            }

            var open = set.Open.Select(window => (IAppWindow)window.Handle).Where(handle => handle.IsOpen);
            return set.Main is { IsOpen: true } main ? [main, .. open] : [.. open];
        }
    }

    public event EventHandler<IAppWindow>? Opened;

    public event EventHandler<IAppWindow>? Closed;

    /// <summary>The host's windows; called once per app, before its pages can call.</summary>
    public void Use(AppWindowSet set)
    {
        if (Interlocked.CompareExchange(ref _set, set, null) is null)
        {
            set.Opened += handle => Raise(Opened, handle);
            set.WindowClosed += handle => Raise(Closed, handle);
        }
    }

    public async Task<Result<IAppWindow, WindowOpenError>> OpenAsync(string kind, string? parameters = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        var bytes = Params(parameters);
        if (_set is not { } set)
        {
            return new WindowOpenError.NotOpened("This host opens no windows of the app: a test without the Window surface, or a browser on loopback.");
        }

        var (result, window) = await set.OpenFromAppAsync(kind, bytes, cancellationToken);
        return result.Outcome switch
        {
            OpenOutcome.Opened or OpenOutcome.Focused when window is not null => window.Handle,
            OpenOutcome.UnknownKind => throw new ArgumentException($"The app declares no window kind '{kind}'.", nameof(kind)),
            OpenOutcome.WrongKind => throw new ArgumentException(
                $"The window kind '{kind}' is a modal: a page shows it with windows.showModal, which waits for its result.", nameof(kind)),
            _ => new WindowOpenError.NotOpened(result.Reason ?? "The window could not be opened."),
        };
    }

    /// <summary>The params as their UTF-8 bytes: a JSON object, at most 64 KB; empty for none.</summary>
    private static ReadOnlyMemory<byte> Params(string? parameters)
    {
        if (parameters is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(parameters);
        if (bytes.Length > WindowCommands.MaxParamsBytes)
        {
            throw new ArgumentException($"A window's params are at most {WindowCommands.MaxParamsBytes / 1024} KB of JSON; these are {bytes.Length} bytes.", nameof(parameters));
        }

        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A window's params are a JSON object, such as {\"tab\":\"connections\"}.", nameof(parameters));
            }
        }
        catch (JsonException error)
        {
            throw new ArgumentException($"A window's params are not JSON: {error.Message}", nameof(parameters), error);
        }

        return bytes;
    }

    /// <summary>The app's handlers run on the pool: a window's own thread never waits for them.</summary>
    private void Raise(EventHandler<IAppWindow>? handlers, IAppWindow window)
    {
        if (handlers is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => handlers(this, window));
        }
    }
}
