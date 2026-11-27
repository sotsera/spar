using Spar.Runtime;

namespace Spar.Hosting;

/// <summary>
/// A desktop host that can open the app's real window inside a test process (plan section 5, level 5, the
/// <c>Window</c> surface). <c>Spar.Windows</c>'s and <c>Spar.Linux</c>'s hosts implement it; only
/// <see cref="SparTestSession"/> calls it, behind the test-host feature switch, so an app's executable keeps neither the
/// call nor the implementation.
/// </summary>
internal interface ISparTestWindowHost
{
    /// <summary>
    /// Opens the app's window for <paramref name="running"/>, whose services the test host built, on the test process's
    /// one UI thread. Returns at once; <see cref="ISparTestWindow.Opened"/> completes when the first page has loaded.
    /// Throws <see cref="SparTestWindowUnavailableException"/> when this machine cannot run window tests.
    /// </summary>
    ISparTestWindow Open(SparApp app, SparRunningApp running, SparTestWindowRequest request);
}

/// <summary>What the test host asks of the window it opens.</summary>
internal sealed class SparTestWindowRequest
{
    /// <summary>The folder under which the test process keeps its shared browser profiles (Windows; each run its own root).</summary>
    public required string ProfilesFolder { get; init; }

    /// <summary>
    /// The folder under which the test process keeps its private home and XDG folders (Linux; plan section 5, "Linux
    /// isolation"; each run its own root).
    /// </summary>
    public required string HomesFolder { get; init; }

    /// <summary>The page engine and the receiver of the page's events, installed before the first navigation.</summary>
    public required SparTestAutomation Automation { get; init; }

    /// <summary>
    /// Who holds the window, for the Linux test process's one-scheme-at-a-time gate: the test that opens it (named by the
    /// test framework's hook), or its test app. A holder with a window of the shown scheme open opens more at once.
    /// </summary>
    public object? Holder { get; init; }

    /// <summary>Each of the host's start-up and shutdown phases as it is reached, for Spar's own host tests (UI thread).</summary>
    public Action<string>? OnPhase { get; init; }

    /// <summary>
    /// The colour scheme the window and its page get: true dark, false light (the default of window tests, so screenshots
    /// match on every machine), null the system's or the app's saved choice, as an app's window does.
    /// </summary>
    public bool? Dark { get; init; }

    /// <summary>
    /// For a window the app's page opens, of one of its kinds (kind, id): the new window's request, with a page driver of
    /// its own, and what the test host does once the window exists (it attaches its page). Null where a test cannot reach
    /// such windows: opening one then fails with <c>notOpened</c>.
    /// </summary>
    public Func<string, string, (SparTestWindowRequest Request, Action<ISparTestWindow> Opened)>? ForAppWindow { get; init; }
}

/// <summary>
/// The page driver's part of a test window: the engine script, run at the start of every document in an isolated
/// script world of its own, and the receiver of the page's events.
/// </summary>
/// <param name="EngineScript">The engine (<c>Spar.Testing</c>'s <c>PageEngine.js</c>), the same file on both hosts.</param>
/// <param name="World">The isolated world's name: the page's scripts can neither see it nor change what it uses.</param>
/// <param name="Events">The page's events; raised on the window's UI thread, so each only copies its data out.</param>
internal sealed record SparTestAutomation(string EngineScript, string World, ISparTestPageEvents Events)
{
    /// <summary>
    /// A script of the page's own world, run at the start of every document before the page's scripts: a scene's fixed
    /// clock (<c>Spar.Testing</c>'s <c>SparScene</c>). Null for an ordinary window test.
    /// </summary>
    public string? PageScript { get; init; }

    /// <summary>
    /// The page's viewport in CSS pixels at a device scale of 1, whatever the monitor's scale: a scene's fixed size. Null
    /// keeps the window's own size and the monitor's scale.
    /// </summary>
    public (int Width, int Height)? Viewport { get; init; }

    /// <summary>Whether the page sees <c>prefers-reduced-motion: reduce</c> (Windows; WebKitGTK cannot emulate it).</summary>
    public bool ReducedMotion { get; init; }
}


/// <summary>The page's events in a test window, raised on the window's UI thread: each must only record its data.</summary>
internal interface ISparTestPageEvents
{
    /// <summary>The main frame committed a new document (<paramref name="newDocument"/>) or changed its address within one.</summary>
    void Navigated(string url, bool newDocument);

    /// <summary>The page wrote to its console: the method (<c>log</c>, <c>error</c>, <c>warning</c>…) and the text.</summary>
    void Console(string type, string text);

    /// <summary>An uncaught exception or unhandled rejection of the page, with its stack when there is one.</summary>
    void PageError(string text);

    /// <summary>A script dialog (<c>alert</c>, <c>confirm</c>, <c>prompt</c>, <c>beforeunload</c>): true accepts it.</summary>
    bool Dialog(string type, string message);
}

/// <summary>A window test's close request (<c>app.Window.CloseAsync</c>).</summary>
/// <param name="Reason">Why the window closes, as the guards read it.</param>
/// <param name="Automated">Whether the guards see the automated mode, where an ask counts as close.</param>
/// <param name="CloseAnyway">
/// What the test answers in place of a native dialog: true "Close anyway", false "Keep open" (and "Wait" for a page
/// that does not answer); null when it gave no answer, which only the automated mode allows.
/// </param>
internal sealed record SparTestClose(CloseReason Reason, bool Automated, bool? CloseAnyway);

/// <summary>What a window test's close request came to.</summary>
/// <param name="Closed">The window closed; false when a guard or the test's answer kept it open.</param>
/// <param name="Question">The last question a guard asked, null when none did.</param>
/// <param name="Unanswered">The page did not answer, and "Wait / Close anyway" was asked.</param>
internal sealed record SparTestCloseReport(bool Closed, string? Question, bool Unanswered);

/// <summary>A window as the OS has it (<see cref="ISparTestWindow.NativeStateAsync"/>).</summary>
internal sealed record SparTestWindowState(string Title, int Width, int Height, bool Minimized, bool Maximized);

/// <summary>One trusted key event: the key's DOM name and code, its Windows virtual key, the modifiers held, and its text.</summary>
/// <param name="Down">A press; false for the release.</param>
/// <param name="Key">The DOM <c>key</c>: <c>"a"</c>, <c>"A"</c>, <c>"Enter"</c>, <c>"Shift"</c>, or any one character.</param>
/// <param name="Code">The DOM <c>code</c> on the US layout (<c>"KeyA"</c>, <c>"Digit1"</c>, <c>"ShiftLeft"</c>); empty for a character the layout has no key for.</param>
/// <param name="VirtualKey">The Windows virtual key code; 0 for a character the layout has no key for.</param>
/// <param name="Modifiers">The modifiers held: Alt 1, Control 2, Meta 4, Shift 8.</param>
/// <param name="Text">The text the press types; null when it types none.</param>
internal readonly record struct SparTestKey(bool Down, string Key, string Code, int VirtualKey, int Modifiers, string? Text);

/// <summary>What a trusted pointer event does.</summary>
internal enum SparTestPointerKind
{
    Move,
    Press,
    Release,
    Wheel,
}

/// <summary>One trusted pointer event of the left button, at a point of the page's viewport in CSS pixels.</summary>
/// <param name="ClickCount">The press's or release's place within a multiple click (1, 2…); 0 for a move or the wheel.</param>
internal readonly record struct SparTestPointer(SparTestPointerKind Kind, float X, float Y, int ClickCount = 0, float DeltaX = 0, float DeltaY = 0);

/// <summary>
/// The app's window in a test process, as the page driver of <c>Spar.Testing</c> uses it: the page engine's requests in
/// its isolated world, script in the page's own world, navigation, trusted input, screenshots, the renderer processes
/// and the window's lifetime. Each host keeps its own mechanism underneath (Windows the WebView's in-process DevTools
/// Protocol session, Linux WebKitGTK's API and XTEST). Every member may be called from any thread; the window's own work
/// runs on its UI thread.
/// </summary>
internal interface ISparTestWindow
{
    /// <summary>Completes when the first navigation has completed, or fails with why the window could not open.</summary>
    Task Opened { get; }

    /// <summary>Completes with the window's exit code once its teardown has finished and the window is gone.</summary>
    Task<int> Closed { get; }

    /// <summary>The page's origin: <c>https://app.localhost</c> on Windows, <c>spar://app</c> on Linux.</summary>
    Uri Origin { get; }

    /// <summary>
    /// One engine request (JSON) in the current document's isolated world, waiting up to <paramref name="wait"/> for a
    /// document whose engine can answer; the answer as JSON, or null when none could (a navigation in progress, a world
    /// just destroyed): the caller tries again.
    /// </summary>
    Task<string?> EngineAsync(string request, TimeSpan wait);

    /// <summary>
    /// Runs the expression in the page's own world, calling it when it is a function and awaiting a promise; its value
    /// as JSON, or null when it is <c>undefined</c>. A script exception is a <see cref="SparTestScriptException"/>.
    /// </summary>
    Task<string?> EvaluateAsync(string expression);

    /// <summary>Opens <paramref name="address"/> (the host's navigation lock still decides on it), or reloads when null.</summary>
    Task NavigateAsync(string? address);

    /// <summary>A PNG of the viewport, or of <paramref name="clip"/> (x, y, width, height) in CSS pixels.</summary>
    Task<byte[]> ScreenshotAsync((double X, double Y, double Width, double Height)? clip);

    /// <summary>A trusted key event to the page's focused element; completes once the page received it.</summary>
    Task KeyAsync(SparTestKey key);

    /// <summary>Inserts text where the focus is, without key events, as an input method does (<c>input</c> events only).</summary>
    Task InsertTextAsync(string text);

    /// <summary>A trusted pointer event; completes once the page received it.</summary>
    Task PointerAsync(SparTestPointer pointer);

    /// <summary>
    /// Files dropped from the OS at (<paramref name="x"/>, <paramref name="y"/>) in CSS pixels: a drag that enters, hovers
    /// and drops through the engine's own drag and drop; completes once the drop was made.
    /// </summary>
    Task DropFilesAsync(float x, float y, IReadOnlyList<string> paths);

    /// <summary>The ids of the renderer (web) processes that render the page now.</summary>
    Task<IReadOnlyList<int>> RendererProcessesAsync();

    /// <summary>
    /// A close of the window by the user (<c>WM_CLOSE</c> on Windows, GTK's close request on Linux): the close request,
    /// the guards, then the teardown.
    /// </summary>
    Task PostCloseMessageAsync();

    /// <summary>
    /// A close request with the test's reason, mode and answers, which join an open one. Completes when the request
    /// ends: kept, or the window's teardown done.
    /// </summary>
    Task<SparTestCloseReport> RequestCloseAsync(SparTestClose close);

    /// <summary>The test's end: the teardown at once, without asking the guards. Completes when the window is gone.</summary>
    Task<int> CloseAsync();

    /// <summary>Sends a window message to the app's window on its UI thread and returns the window procedure's result (Windows only).</summary>
    Task<nint> SendMessageAsync(uint message, nint wParam, nint lParam);

    /// <summary>
    /// The end of the OS session as the host receives it: Windows' <c>WM_QUERYENDSESSION</c> then <c>WM_ENDSESSION</c> for a
    /// log-off; on Linux the path <c>SIGTERM</c> starts, without the signal (the test process's signals are its runner's).
    /// Completes when the session end returned, the point after which the OS may end the process; true when the session
    /// end was allowed (Windows' answer to the query; always on Linux).
    /// </summary>
    Task<bool> EndSessionAsync();

    /// <summary>
    /// The window's own colours: whether its frame is dark (Windows' <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>, GTK's dark
    /// preference on Linux) and the colour it paints behind the page, as <c>0x00BBGGRR</c>.
    /// </summary>
    Task<(bool DarkTitleBar, uint WindowColour)> NativeThemeAsync();

    /// <summary>
    /// Whether the window takes the user's input: false while a modal of its own is open (Windows' <c>IsWindowEnabled</c>;
    /// on Linux, whether a modal window is transient for it, which GTK keeps its input from).
    /// </summary>
    Task<bool> TakesInputAsync();

    /// <summary>The window as the OS has it: its title, its page's size in CSS pixels, minimized, maximized.</summary>
    Task<SparTestWindowState> NativeStateAsync();
}

/// <summary>
/// A request the page's window could not run because its document or world went away between the request and the
/// answer (a navigation, a re-render): the page driver tries again. <see cref="Error"/> is the host's own description.
/// </summary>
internal sealed class SparTestCallException(string operation, string? error)
    : Exception($"{operation} failed{(string.IsNullOrEmpty(error) ? "" : ": " + error)}")
{
    public string Operation { get; } = operation;

    public string? Error { get; } = error;
}

/// <summary>Script the test ran in the page threw: the exception's description, with its stack when there is one.</summary>
internal sealed class SparTestScriptException(string description) : Exception(description);

/// <summary>
/// This machine cannot run window tests: the host's start-up check's own message (no WebView2 Runtime, no WebKitGTK at
/// the supported minimum, no Xvfb, user namespaces blocked for the web process's sandbox). Spar's own window tests skip
/// on it, and fail on CI.
/// </summary>
internal sealed class SparTestWindowUnavailableException(string message) : InvalidOperationException(message);
