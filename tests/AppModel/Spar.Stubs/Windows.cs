namespace Spar;

/// <summary>The app's windows for its C# code; injectable.</summary>
public interface IWindows
{
    IAppWindow Main { get; }

    IReadOnlyList<IAppWindow> All { get; }

    event EventHandler<IAppWindow>? Opened;

    event EventHandler<IAppWindow>? Closed;

    /// <summary>Opens a window of a kind with a JSON object as its params; a single-instance kind focuses the open one.</summary>
    Task<Result<IAppWindow, WindowOpenError>> OpenAsync(string kind, string? parameters = null, CancellationToken cancellationToken = default);
}

/// <summary>One of the app's windows.</summary>
public interface IAppWindow
{
    string Id { get; }

    string Kind { get; }

    bool IsOpen { get; }

    void Focus();

    void Minimize();

    void Maximize();

    void Restore();

    void SetSize(int width, int height);

    void SetTitle(string title);

    /// <summary>Asks the window to close through its guards; true once it closed.</summary>
    Task<bool> CloseAsync();

    /// <summary>Turns capture protection on or off at run time; returns whether it took effect.</summary>
    bool SetCaptureProtection(bool enabled);
}

/// <summary>Why OpenAsync opened no window.</summary>
public closed record WindowOpenError
{
    public sealed record NotOpened(string Reason) : WindowOpenError;
}
