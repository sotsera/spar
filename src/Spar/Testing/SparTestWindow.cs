using Spar.Runtime;

namespace Spar.Hosting;

/// <summary>
/// A desktop host that can open the app's real window inside a test process (plan section 5, level 5, the
/// <c>Window</c> surface). <c>Spar.Windows</c>'s host implements it; only <see cref="SparTestSession"/> calls it, behind
/// the test-host feature switch, so an app's executable keeps neither the call nor the implementation.
/// </summary>
internal interface ISparTestWindowHost
{
    /// <summary>
    /// Opens the app's window for <paramref name="running"/>, whose services the test host built, on the test process's
    /// one UI thread. Returns at once; <see cref="ISparTestWindow.Opened"/> completes when the first page has loaded.
    /// </summary>
    ISparTestWindow Open(SparApp app, SparRunningApp running, SparTestWindowRequest request);
}

/// <summary>What the test host asks of the window it opens.</summary>
internal sealed class SparTestWindowRequest
{
    /// <summary>The folder under which the test process keeps its shared browser profiles (each run its own root).</summary>
    public required string ProfilesFolder { get; init; }

    /// <summary>
    /// Runs on the window's UI thread before the first navigation, after the host's own DevTools calls: the page
    /// driver installs itself here. Its work must be short; it may return a task that the navigation waits for.
    /// </summary>
    public Func<ISparTestWindow, Task>? BeforeFirstNavigation { get; init; }

    /// <summary>Each of the host's start-up and shutdown phases as it is reached, for Spar's own host tests (UI thread).</summary>
    public Action<string>? OnPhase { get; init; }
}

/// <summary>
/// The app's window in a test process: the page's in-process DevTools Protocol session, which the page driver of
/// <c>Spar.Testing</c> uses, and the window's lifetime. Every member may be called from any thread; the window's own
/// work runs on its UI thread.
/// </summary>
internal interface ISparTestWindow
{
    /// <summary>Completes when the first navigation has completed, or fails with why the window could not open.</summary>
    Task Opened { get; }

    /// <summary>Completes with the window's exit code once its teardown has finished and the window is gone.</summary>
    Task<int> Closed { get; }

    /// <summary>The page's origin, for example <c>https://app.localhost</c>.</summary>
    Uri Origin { get; }

    /// <summary>Calls a DevTools Protocol method on the page's session; completes with the result object as JSON.</summary>
    Task<string> CallDevToolsAsync(string method, string parametersJson);

    /// <summary>
    /// Receives a DevTools Protocol event of the page's session until the returned object is disposed. The callback
    /// runs on the UI thread: it must only copy the JSON out.
    /// </summary>
    Task<IDisposable> SubscribeAsync(string eventName, Action<string> onEvent);

    /// <summary>The ids of the renderer processes that render the page now.</summary>
    Task<IReadOnlyList<int>> RendererProcessesAsync();

    /// <summary>A close of the window by the user (<c>WM_CLOSE</c>): the close request, the guards, then the teardown.</summary>
    Task RequestCloseAsync();

    /// <summary>The test's end: the teardown at once, without asking the guards. Completes when the window is gone.</summary>
    Task<int> CloseAsync();

    /// <summary>Sends a window message to the app's window on its UI thread and returns the window procedure's result.</summary>
    Task<nint> SendMessageAsync(uint message, nint wParam, nint lParam);
}

/// <summary>
/// A DevTools Protocol call the page's session refused: the method, WebView2's failure code, and the protocol's error
/// object as JSON when WebView2 passed one (such as <c>{"code":-32000,"message":"Cannot find context with specified id"}</c>).
/// </summary>
internal sealed class SparDevToolsException(string method, int code, string? error)
    : Exception($"{method} failed with 0x{code:X8}{(string.IsNullOrEmpty(error) ? "" : ": " + error)}")
{
    public string Method { get; } = method;

    public int Code { get; } = code;

    public string? Error { get; } = error;
}
