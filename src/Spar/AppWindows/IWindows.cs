namespace Spar;

/// <summary>
/// The app's windows, for its C# code (decision "M3d's decide-first questions", item 8); injectable, one per app. The
/// main window, and every window of the app's kinds that is open, whether its page or the app opened it. Every member
/// may be called from any thread; the host does the work on its UI thread, and the events are raised on the thread
/// pool, never on the UI thread.
/// </summary>
public interface IWindows
{
    /// <summary>The main window; it throws before the app's window exists (a test without the Window surface).</summary>
    IAppWindow Main { get; }

    /// <summary>The open windows: the main one first, then the others in the order they opened.</summary>
    IReadOnlyList<IAppWindow> All { get; }

    /// <summary>A window of the app's kinds opened.</summary>
    event EventHandler<IAppWindow>? Opened;

    /// <summary>A window of the app's kinds closed; the main window's close ends the app.</summary>
    event EventHandler<IAppWindow>? Closed;

    /// <summary>
    /// Opens a window of a kind <c>Program.cs</c> declares, with <paramref name="parameters"/> as its params (a JSON
    /// object, at most 64 KB, which its page reads with <c>windows.params()</c>); a single-instance kind that is open is
    /// focused, and that window is the result. A modal kind is shown by a page with <c>windows.showModal</c>, which waits
    /// for its result: it is not opened from C#. An unknown kind, a modal one or params that are not a JSON object
    /// throw <see cref="ArgumentException"/>.
    /// </summary>
    Task<Result<IAppWindow, WindowOpenError>> OpenAsync(string kind, string? parameters = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// One of the app's windows (<see cref="IWindows"/>, <see cref="ISparCall.Window"/>). Once it closed, every member does
/// nothing and <see cref="CloseAsync"/> answers true.
/// </summary>
public interface IAppWindow
{
    /// <summary><c>main</c>, or its kind's name with a number: <c>settings-1</c> for the first.</summary>
    string Id { get; }

    /// <summary><c>main</c>, or the kind's name as <c>Program.cs</c> declares it.</summary>
    string Kind { get; }

    /// <summary>Whether the window is open: false from the start of its teardown.</summary>
    bool IsOpen { get; }

    /// <summary>Brings the window to the front, restoring it when it is minimized; the OS may only flash it.</summary>
    void Focus();

    void Minimize();

    void Maximize();

    /// <summary>A minimized window back to its place, or a maximized one to its normal size.</summary>
    void Restore();

    /// <summary>The size of the window's page, in CSS pixels at a zoom of 1, kept within its minimum size.</summary>
    void SetSize(int width, int height);

    /// <summary>The window's title, as the taskbar and the window switcher show it.</summary>
    void SetTitle(string title);

    /// <summary>
    /// Asks the window to close, as the app's own code (<see cref="CloseReason.App"/>): its own modals first, then its
    /// page's guard, then <c>OnCloseRequested</c>. True once it closed, false when a guard or the user kept it.
    /// </summary>
    Task<bool> CloseAsync();
}

/// <summary>Why <see cref="IWindows.OpenAsync"/> opened no window.</summary>
public closed record WindowOpenError
{
    /// <summary>No window was opened: the app is closing, the host has no windows of its own, or it failed; the reason says which.</summary>
    public sealed record NotOpened(string Reason) : WindowOpenError;
}
