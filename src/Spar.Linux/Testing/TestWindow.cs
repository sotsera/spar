using System.Diagnostics;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Runtime;
using static Spar.Linux.Testing.TestNative;

namespace Spar.Linux.Testing;

/// <summary>
/// The app's real window in a Linux test process (plan section 5, level 5, the <c>Window</c> surface): the same
/// <see cref="AppWindow"/> an app runs, with its WebKit view, bridge, page security, heartbeat and two-stage close, on
/// the test process's shared main loop (<see cref="TestWindowProcess"/>), under the test's private Xvfb. Its view has an
/// ephemeral network session and a web context of its own; nothing shows a dialog (the automated mode); its log is the
/// app's own log file, never the process's standard error. The page driver's engine runs in its isolated world
/// (<see cref="TestPage"/>), and trusted input goes through XTEST (<see cref="XInput"/>), each key and pointer event
/// acknowledged by the page before the call returns, as a DevTools input call is on Windows.
/// <para>
/// When the window is gone, <see cref="Closed"/> fails if a web process that served its page is still running a few
/// seconds later: the leftover-process check of plan section 5.
/// </para>
/// </summary>
internal sealed class TestWindow : ISparTestWindow, IAppWindowOwner
{
    /// <summary>How long the page's web processes get to exit once the window is gone.</summary>
    private static readonly TimeSpan RendererExitLimit = TimeSpan.FromSeconds(5);

    /// <summary>How long an input event may take to reach the page; one that never does (GTK kept it) is not waited for longer.</summary>
    private const int InputArrival = 1_000;

    /// <summary>How long a window may hold the first-load turn, should its first commit never come.</summary>
    private static readonly TimeSpan FirstLoadLimit = TimeSpan.FromSeconds(15);

    /// <summary>The least an engine call is waited for, even at the end of an action's time.</summary>
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(100);

    /// <summary>How often a waiting engine call checks whether a navigation replaced its document.</summary>
    private static readonly TimeSpan DocumentCheck = TimeSpan.FromMilliseconds(50);

    private readonly TestWindowProcess _process;
    private readonly SparApp _app;
    private readonly SparRunningApp _running;
    private readonly SparTestWindowRequest _request;
    private readonly HostLog _log = new(standardError: false);
    private readonly PageHangHook _hangHook;
    private readonly WindowTheme _theme;
    private readonly SparPageSecurity _security;
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<int> _renderers = [];
    private AppWindow? _window;
    private TestPage? _page;
    private int _holdsFirstLoad;
    private int _gone;
    private int _inScheme;
    private int _notedSilence;
    private bool _closedBeforeStart;
    private (int Slot, int X, int Y)? _place;

    private TestWindow(TestWindowProcess process, SparApp app, SparRunningApp running, SparTestWindowRequest request)
    {
        _process = process;
        _app = app;
        _running = running;
        _request = request;
        _log.UseAppLogging(running.Services);
        _hangHook = new PageHangHook(app.Builder.PageHangHandler, _log.Logger);
        _security = PageSecuritySetup.Resolve(app, _log.Logger);
        process.CheckWebAssembly(_security);

        // Off the loop: the system's scheme reads the Settings portal of the test's private session, which has none.
        _theme = request.Dark is { } dark
            ? WindowTheme.Fixed(app.Builder.MainWindow, dark)
            : WindowTheme.Read(app.Builder.MainWindow, running.Folders.DataRoot);
    }

    public Task Opened => _opened.Task;

    public Task<int> Closed => _closed.Task;

    public Uri Origin { get; } = new(SchemeServer.Origin);

    /// <summary>Opens the window of <paramref name="app"/> on the test process's main loop; returns at once.</summary>
    public static TestWindow Open(SparApp app, SparRunningApp running, SparTestWindowRequest request)
    {
        var process = TestWindowProcess.Get(app, request);
        var window = new TestWindow(process, app, running, request);
        process.Ui.Post(() => _ = window.StartAsync(), "test window");
        return window;
    }

    public async Task<string?> EngineAsync(string request, TimeSpan wait)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            // An answer that never comes (a document that stopped running its frames) is a retry, never a hang.
            var document = _page?.Documents;
            var call = OnLoopAsync(() => Page.EngineAsync(request));
            var waited = await AwaitEngineAsync(call, document, started, wait);
            if (waited == EngineWait.Silent)
            {
                await NoteSilentEngineAsync(request, document);
                return null;
            }

            var answer = waited == EngineWait.Answered ? await call : null;
            if (answer is not null || Stopwatch.GetElapsedTime(started) >= wait || _closed.Task.IsCompleted)
            {
                return answer;
            }

            // No document could answer yet (a navigation, a reload after a lost web process): it soon will.
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Waits for one engine call: answered; silent past the wait (and at least <see cref="MinimumWait"/>); or sent to a
    /// document a navigation replaced meanwhile, which never answers it (a click on a link, measured on CI's Linux
    /// container: the request waited out its whole wait, then was taken for a page that stopped answering).
    /// </summary>
    private async Task<EngineWait> AwaitEngineAsync(Task<string?> call, int? document, long started, TimeSpan wait)
    {
        var callStarted = Stopwatch.GetTimestamp();
        while (true)
        {
            var forWait = wait - Stopwatch.GetElapsedTime(started);
            var forMinimum = MinimumWait - Stopwatch.GetElapsedTime(callStarted);
            var left = forWait > forMinimum ? forWait : forMinimum;
            if (left <= TimeSpan.Zero)
            {
                return EngineWait.Silent;
            }

            if (await Task.WhenAny(call, Task.Delay(left < DocumentCheck ? left : DocumentCheck)) == call)
            {
                return EngineWait.Answered;
            }

            if (_page?.Documents != document)
            {
                return EngineWait.Replaced;
            }
        }
    }

    /// <summary>
    /// An engine call that did not answer in time: what the page's state was, in the app's log (once per window). Not
    /// when a navigation replaced the document it was sent to meanwhile: that document never answers, and nothing is wrong.
    /// </summary>
    private async Task NoteSilentEngineAsync(string request, int? document)
    {
        var state = OnLoopAsync(() => Page.EvaluateAsync("document.visibilityState + ' ' + document.hasFocus() + ' ' + document.readyState"));
        var shown = await Task.WhenAny(state, Task.Delay(1_000)) == state && state.IsCompletedSuccessfully ? state.Result : "no answer";
        if (_page?.Documents != document || Interlocked.Exchange(ref _notedSilence, 1) != 0)
        {
            return;
        }

        _log.Warn($"The page engine did not answer {request} in time; the page: {shown}.");
    }

    public Task<string?> EvaluateAsync(string expression) => OnLoopAsync(() => Page.EvaluateAsync(expression));

    public Task NavigateAsync(string? address) => OnLoopAsync(() =>
    {
        Page.Navigate(address);
        return Task.FromResult(true);
    });

    public Task<byte[]> ScreenshotAsync((double X, double Y, double Width, double Height)? clip) =>
        OnLoopAsync(() => Page.ScreenshotAsync(clip));

    public async Task KeyAsync(SparTestKey key)
    {
        // The key's own event, keydown or keyup, is waited for: any trusted input once counted, a pointer event the
        // engine did not send included.
        var type = key.Down ? "keydown" : "keyup";
        var (before, focused) = await InputCountAsync(type);
        var (window, _, _) = await GeometryAsync();
        if (!focused)
        {
            // A key GTK reads before the window has handled its new focus does not reach the document (measured with
            // parallel windows in the CI container: the 6 keys of 829 that never arrived were all sent while the
            // document lacked the focus). So the focus first, and the key once the document has it.
            await _process.Input.FocusAsync(window);
            await EngineAsync($$"""{"op":"focused","timeout":{{InputArrival}}}""", TimeSpan.FromSeconds(5));
        }

        await _process.Input.KeyAsync(window, key);
        if (await InputArrivedAsync(before, type) is { } answer && answer.Contains("\"seen\":false", StringComparison.Ordinal))
        {
            // Said in the app's log: a test that then fails on a missing character is explained there.
            _log.Warn($"The page did not receive the {type} of {key.Key} within {InputArrival} ms: {answer}.");
        }
    }

    public async Task InsertTextAsync(string text)
    {
        await EngineAsync(InsertTextRequest(text), TimeSpan.FromSeconds(5));
    }

    public async Task PointerAsync(SparTestPointer pointer)
    {
        var before = await InputCountAsync();
        var (window, x, y) = await GeometryAsync();
        if (await _process.Input.PointerAsync(window, x + (int)MathF.Round(pointer.X), y + (int)MathF.Round(pointer.Y), pointer))
        {
            await InputArrivedAsync(before);
        }
    }

    public Task<IReadOnlyList<int>> RendererProcessesAsync()
    {
        lock (_renderers)
        {
            return Task.FromResult<IReadOnlyList<int>>([.. _renderers.Where(id => Directory.Exists($"/proc/{id}"))]);
        }
    }

    public Task PostCloseMessageAsync() => OnLoopAsync(() =>
    {
        _window?.CloseAsUser();
        return Task.FromResult(true);
    });

    public Task<SparTestCloseReport> RequestCloseAsync(SparTestClose close) => OnLoopAsync(async () =>
    {
        var window = _window ?? throw new InvalidOperationException("The app's window is not open yet.");
        var questions = close.CloseAnyway is { } closeAnyway ? new TestAnswers(closeAnyway) : null;
        var report = await window.RequestCloseAsync(close.Reason, close.Automated, questions);
        return new SparTestCloseReport(report.Closed, report.Question, report.Unanswered);
    });

    public Task<int> CloseAsync()
    {
        _process.Ui.Post(() =>
        {
            if (_window is { } window)
            {
                _ = window.TeardownAsync(0);
                return;
            }

            // Still waiting for its colour scheme's turn: it never opens.
            _closedBeforeStart = true;
            _opened.TrySetException(new InvalidOperationException("The app's window was closed before it opened."));
            _closed.TrySetResult(0);
        }, "test window close");
        return _closed.Task;
    }

    public Task<nint> SendMessageAsync(uint message, nint wParam, nint lParam) =>
        throw new PlatformNotSupportedException("Window messages are Windows'; a Linux window has none.");

    /// <summary>
    /// The session end <c>SIGTERM</c> starts in an app (<see cref="SessionEnd"/>), without the signal: the window's
    /// session-end close, then the app's services disposed within the budget, as an app's are; the test host's later
    /// disposal finds them done. Its final line goes to the app's log, and its hold to the phases.
    /// </summary>
    public async Task<bool> EndSessionAsync()
    {
        var window = _window ?? throw new InvalidOperationException("The app's window is not open yet.");
        var start = Stopwatch.GetTimestamp();
        var outcome = await SessionEnd.RunAsync(_process.Ui, window, DisposeServicesAsync, start);
        _log.Step($"session end: {outcome.Text}; held {outcome.HeldMilliseconds:0} ms");
        _request.OnPhase?.Invoke($"session end held {outcome.HeldMilliseconds:0} ms");
        return true;
    }

    private async Task DisposeServicesAsync()
    {
        try
        {
            await _running.DisposeAsync();
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly at session end", exception);
        }
    }

    public unsafe Task<(bool DarkTitleBar, uint WindowColour)> NativeThemeAsync() => OnLoopAsync(() =>
    {
        var window = _window?.Handle ?? 0;
        if (window == 0)
        {
            throw new InvalidOperationException("The app's window is not open.");
        }

        GValue prefer = default;
        GLib.g_value_init(&prefer, GLib.TypeBoolean);
        GLib.g_object_get_property(Gtk.gtk_settings_get_for_display(Gtk.gtk_widget_get_display(window)), "gtk-application-prefer-dark-theme", &prefer);
        var dark = GLib.g_value_get_boolean(&prefer) != 0;
        GLib.g_value_unset(&prefer);
        var colour = _theme.Background;
        return Task.FromResult((dark, ((uint)colour.Blue << 16) | ((uint)colour.Green << 8) | colour.Red));
    });

    void IAppWindowOwner.TeardownStarted() => _request.OnPhase?.Invoke("closing");

    /// <summary>The window is gone and its web processes have exited (loop thread): the rest of the stop runs off the loop.</summary>
    void IAppWindowOwner.WindowClosed()
    {
        Volatile.Write(ref _gone, 1);
        ReleaseFirstLoad();
        LeaveScheme();
        _opened.TrySetException(new InvalidOperationException(
            $"The app's window closed before its page loaded (exit code {_window?.ExitCode}); its log is in the test's logs folder."));
        _ = Task.Run(FinishAsync);
    }

    /// <summary>This window's web processes still running (off the loop); the test process's other windows have their own.</summary>
    List<(int Id, string Name)> IAppWindowOwner.WebKitProcesses()
    {
        int[] noted;
        lock (_renderers)
        {
            noted = [.. _renderers];
        }

        return [.. WebProcesses.Below(Environment.ProcessId).Where(process => noted.Contains(process.Id))];
    }

    /// <summary>Who holds this window at the scheme gate: the request's holder, or the app.</summary>
    private object SchemeHolder => _request.Holder ?? _app;

    private TestPage Page => _page is { } page && _window is { ViewHandle: not 0 } ? page : throw new InvalidOperationException(_closed.Task.IsCompleted || _window?.IsClosing == true
        ? "The app's window is closed (app.Window.CloseAsync or the test's end closed it): its page can no longer be driven."
        : "The app's page is not open: its window is starting.");

    /// <summary>The window and its view on the loop (the shared environment is ready: "environment"), then its first load.</summary>
    private async Task StartAsync()
    {
        try
        {
            // The process's windows show one colour scheme at a time (GTK's dark preference is the process's).
            await _process.EnterSchemeAsync(_request.Dark ?? _theme.AppChoosesDark ?? false, SchemeHolder);
            Volatile.Write(ref _inScheme, 1);
            if (_closedBeforeStart)
            {
                LeaveScheme();
                return;
            }

            _request.OnPhase?.Invoke("environment");
            _place = _process.RentPlace();
            var source = new BrowserSource { BeforeFirstLoad = SetUpAsync, Realized = Place };
            var title = _app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
            var profile = new BrowserProfile(_running.Folders.Profile);
            _window = new AppWindow(
                _app, _security, _log, automated: true, title, _running.Folders, profile, _hangHook, promptAnswers: null, this, source);
            _request.OnPhase?.Invoke("controller requested");
            _window.Start(_process.Ui, _theme, Task.FromResult(_running));
        }
        catch (Exception exception)
        {
            ReleaseFirstLoad();
            LeaveScheme();
            _opened.TrySetException(exception);
            _closed.TrySetException(exception);
        }
    }

    /// <summary>The window no longer holds the process to its colour scheme (loop thread).</summary>
    private void LeaveScheme()
    {
        if (Interlocked.Exchange(ref _inScheme, 0) == 1)
        {
            _process.LeaveScheme(SchemeHolder);
        }

        if (_place is { } place)
        {
            _place = null;
            _process.ReturnPlace(place.Slot);
        }
    }

    /// <summary>The realized window, before it is shown (loop thread): moved to its own place of the test display.</summary>
    private void Place(nint window)
    {
        if (_place is not { } place)
        {
            return;
        }

        var surface = X11.gtk_native_get_surface(window);
        var display = X11.gdk_x11_display_get_xdisplay(X11.gdk_surface_get_display(surface));
        XMoveWindow(display, X11.gdk_x11_surface_get_xid(surface), place.X, place.Y);
    }

    /// <summary>
    /// The view exists ("controller"): the page driver's scripts and signals, then the first-load turn, so the web process
    /// that appears for this page is known to be its own.
    /// </summary>
    private async Task SetUpAsync(nint view, nint content)
    {
        _request.OnPhase?.Invoke("controller");
        if (_request.Automation.Viewport is { } viewport && _window is { } window)
        {
            // A scene's page: the view exactly its size (the private Xvfb's scale is 1), the window as small as that
            // allows, so the title bar adds to it rather than taking from it.
            Gtk.gtk_widget_set_size_request(view, viewport.Width, viewport.Height);
            window.FitToContent();
        }

        var page = TestPage.Install(view, content, _request.Automation, phase => _request.OnPhase?.Invoke(phase), OnCommitted);
        page.FirstLoaded += () =>
        {
            _opened.TrySetResult();
            ReleaseFirstLoad();
        };
        page.FirstLoadFailed += why =>
        {
            ReleaseFirstLoad();
            _opened.TrySetException(new InvalidOperationException($"The app's page failed to load ({why}); its log is in the test's logs folder."));
        };
        _page = page;
        if (!await _process.FirstLoads.WaitAsync(FirstLoadLimit))
        {
            _log.Warn($"The first-load turn was not free within {FirstLoadLimit.TotalSeconds:0} s: this window's web process may be told apart wrongly.");
            return;
        }

        Interlocked.Exchange(ref _holdsFirstLoad, 1);
        if (Volatile.Read(ref _gone) == 1)
        {
            // The window closed while it waited for its turn (a close during its creation): the turn goes on at once.
            ReleaseFirstLoad();
        }
    }

    /// <summary>A document committed (loop thread): the web processes that appeared for it are this window's.</summary>
    private void OnCommitted(bool first)
    {
        var holds = Volatile.Read(ref _holdsFirstLoad) == 1;
        _ = Task.Run(() =>
        {
            var claimed = _process.Claim(firstLoad: first && holds);
            lock (_renderers)
            {
                _renderers.UnionWith(claimed);
            }

            if (first)
            {
                ReleaseFirstLoad();
            }
        });
    }

    private void ReleaseFirstLoad()
    {
        if (Interlocked.Exchange(ref _holdsFirstLoad, 0) == 1)
        {
            _process.FirstLoads.Release();
        }
    }

    private async Task FinishAsync()
    {
        try
        {
            if (_window is { } window)
            {
                await window.StopAsync();
            }

            await _hangHook.StopAsync(TimeSpan.FromSeconds(3));
            var leftovers = await LeftoverRenderersAsync();
            if (leftovers.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Web processes of the app's page were still running {RendererExitLimit.TotalSeconds:0} s after its window closed: {string.Join(", ", leftovers)}.");
            }

            _closed.TrySetResult(_window?.ExitCode ?? 0);
        }
        catch (Exception exception)
        {
            _closed.TrySetException(exception);
        }
        finally
        {
            // Once nothing can call back: the view, its web context and the page's signals are gone with the window.
            _process.Ui.Post(() =>
            {
                _window?.Release();
                _page?.Release();
            }, "test window release");
        }
    }

    private async Task<List<int>> LeftoverRenderersAsync()
    {
        var limit = Stopwatch.GetTimestamp() + (long)(RendererExitLimit.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            var running = (await RendererProcessesAsync()).ToList();
            if (running.Count == 0 || Stopwatch.GetTimestamp() >= limit)
            {
                return running;
            }

            await Task.Delay(20);
        }
    }

    /// <summary>The count of trusted input events the page has received, before one more is sent.</summary>
    private async Task<int> InputCountAsync() => (await InputCountAsync(null)).Count;

    /// <summary>The count of trusted input events of <paramref name="type"/> (all, for null), and whether the document has the focus.</summary>
    private async Task<(int Count, bool Focused)> InputCountAsync(string? type)
    {
        var request = type is null ? """{"op":"inputCount"}""" : $$"""{"op":"inputCount","type":"{{type}}"}""";
        var answer = await EngineAsync(request, TimeSpan.FromSeconds(5));
        if (answer is null)
        {
            return (0, true);
        }

        using var json = System.Text.Json.JsonDocument.Parse(answer);
        var focused = !json.RootElement.TryGetProperty("focused", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.False;
        return (Count(answer), focused);
    }

    /// <summary>
    /// Until the page received more than <paramref name="before"/> trusted input events (of <paramref name="type"/>, when
    /// given), or a second passed; the engine's answer.
    /// </summary>
    private Task<string?> InputArrivedAsync(int before, string? type = null) =>
        EngineAsync(type is null
            ? $$"""{"op":"inputAfter","after":{{before}},"timeout":{{InputArrival}}}"""
            : $$"""{"op":"inputAfter","after":{{before}},"type":"{{type}}","timeout":{{InputArrival}}}""", TimeSpan.FromSeconds(5));

    /// <summary>
    /// The window's X window and where the view's top-left corner is on the screen (loop thread): the window's origin on
    /// the root, its surface's decorations, and the view's place in it; the test display's scale is 1.
    /// </summary>
    private unsafe Task<(nuint Window, int X, int Y)> GeometryAsync() => OnLoopAsync(() =>
    {
        var window = _window?.Handle ?? 0;
        var view = _window?.ViewHandle ?? 0;
        if (window == 0 || view == 0)
        {
            throw new InvalidOperationException("The app's window is not open: input cannot reach its page.");
        }

        var surface = X11.gtk_native_get_surface(window);
        var xid = X11.gdk_x11_surface_get_xid(surface);
        var display = X11.gdk_x11_display_get_xdisplay(X11.gdk_surface_get_display(surface));
        int rootX, rootY;
        nuint child;
        XTranslateCoordinates(display, xid, XDefaultRootWindow(display), 0, 0, &rootX, &rootY, &child);
        double surfaceX, surfaceY;
        gtk_native_get_surface_transform(window, &surfaceX, &surfaceY);
        var origin = default(GraphenePoint);
        GraphenePoint placed;
        if (gtk_widget_compute_point(view, window, &origin, &placed) == 0)
        {
            placed = default;
        }

        return Task.FromResult((xid, rootX + (int)Math.Round(surfaceX + placed.X), rootY + (int)Math.Round(surfaceY + placed.Y)));
    });

    /// <summary>Runs <paramref name="work"/> on the loop; its task's result and failures reach the caller off it.</summary>
    private Task<T> OnLoopAsync<T>(Func<Task<T>> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process.Ui.Post(() => _ = RelayAsync(work, result), "test window call");
        return result.Task;
    }

    private static async Task RelayAsync<T>(Func<Task<T>> work, TaskCompletionSource<T> result)
    {
        try
        {
            result.TrySetResult(await work());
        }
        catch (Exception exception)
        {
            result.TrySetException(exception);
        }
    }

    private static string InsertTextRequest(string text)
    {
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("op", "insertText");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static int Count(string answer)
    {
        using var json = System.Text.Json.JsonDocument.Parse(answer);
        return json.RootElement.TryGetProperty("count", out var count) && count.TryGetInt32(out var number) ? number : 0;
    }

    /// <summary>How one engine call ended (<see cref="AwaitEngineAsync"/>).</summary>
    private enum EngineWait
    {
        Answered,
        Silent,
        Replaced,
    }

    /// <summary>A window test's answer in place of every native dialog of its close request.</summary>
    private sealed class TestAnswers(bool closeAnyway) : ICloseQuestions
    {
        public Task<bool> ConfirmAsync(string question) => Task.FromResult(closeAnyway);

        public Task<bool> CloseUnansweredAsync() => Task.FromResult(closeAnyway);
    }
}
