using System.Diagnostics;
using Spar.Hosting;
using Spar.Runtime;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows.Testing;

/// <summary>
/// The app's real window in a test process (plan section 5, level 5, the <c>Window</c> surface): the same
/// <see cref="AppWindow"/> an app runs, with its WebView, bridge, page security, heartbeat and two-stage close, on the
/// test process's shared UI thread, environment and anchor (<see cref="TestWindowProcess"/>). It is shown offscreen,
/// with no taskbar button, never activated; its controller is InPrivate under a profile name of its own; SmartScreen
/// is off, as in every Spar window; nothing shows a dialog (the automated mode). Its log is the app's own log file,
/// never the process's standard error.
/// <para>
/// When the window is gone, the test host learns it from <see cref="Closed"/>, which fails when a renderer process that
/// served the page is still running a few seconds later: the leftover-process check of plan section 5.
/// </para>
/// </summary>
internal sealed class TestWindow : ISparTestWindow, IAppWindowOwner
{
    /// <summary>How long the page's renderer processes get to exit once the window is gone.</summary>
    private static readonly TimeSpan RendererExitLimit = TimeSpan.FromSeconds(5);

    private readonly TestWindowProcess _process;
    private readonly SparTestWindowRequest _request;
    private readonly HostLog _log = new(standardError: false);
    private readonly PageHangHook _hangHook;
    private readonly AppWindow _window;
    private readonly string _profile;
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<int> _renderers = [];
    private readonly Task<SparRunningApp> _running;
    private readonly WindowTheme _theme;
    private TestPage? _page;

    private TestWindow(TestWindowProcess process, SparApp app, SparRunningApp running, SparTestWindowRequest request)
    {
        _process = process;
        _request = request;
        _running = Task.FromResult(running);
        _profile = process.RentProfile();
        _log.UseAppLogging(running.Services);
        _hangHook = new PageHangHook(app.Builder.PageHangHandler, _log.Logger);
        _theme = request.Dark is { } dark
            ? WindowTheme.Fixed(app.Builder.MainWindow, dark)
            : WindowTheme.Read(app.Builder.MainWindow, running.Folders.DataRoot);

        var source = new BrowserSource
        {
            SharedEnvironment = process.EnvironmentAsync,
            PrivateProfile = _profile,
            BrowserArguments = request.Automation.Viewport is null ? null : "--disable-gpu",
            BeforeFirstNavigation = BeforeFirstNavigationAsync,
            OnPhase = OnPhase,
            ColorSchemeChosen = EmulateSchemeAsync,
        };
        var title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
        var profile = new BrowserProfile(running.Folders.Profile);
        _window = new AppWindow(app, _log, automated: true, title, profile, _hangHook, this, offscreen: true, source);
        Origin = new Uri(PageServer.Origin);
    }

    public Task Opened => _opened.Task;

    public Task<int> Closed => _closed.Task;

    public Uri Origin { get; }

    /// <summary>Opens the window of <paramref name="app"/> on the test process's UI thread; returns at once.</summary>
    public static TestWindow Open(SparApp app, SparRunningApp running, SparTestWindowRequest request)
    {
        var process = TestWindowProcess.Get(request.ProfilesFolder);
        var window = new TestWindow(process, app, running, request);
        process.Ui.Post(window.Start, "test window");
        return window;
    }

    public Task<string?> EngineAsync(string request, TimeSpan wait) => Page.EngineAsync(request, wait);

    public Task<string?> EvaluateAsync(string expression) => Page.EvaluateAsync(expression);

    public Task NavigateAsync(string? address) => Page.NavigateAsync(address);

    public Task<byte[]> ScreenshotAsync((double X, double Y, double Width, double Height)? clip) => Page.ScreenshotAsync(clip);

    public Task KeyAsync(SparTestKey key) => Page.KeyAsync(key);

    public Task DropFilesAsync(float x, float y, IReadOnlyList<string> paths) => Page.DropFilesAsync(x, y, paths);

    public Task InsertTextAsync(string text) => Page.InsertTextAsync(text);

    public Task PointerAsync(SparTestPointer pointer) => Page.PointerAsync(pointer);

    /// <summary>The page's DevTools transport, installed before the first navigation.</summary>
    private TestPage Page => _page ?? throw new InvalidOperationException(_closed.Task.IsCompleted || _window.IsClosing
        ? "The app's window is closed (app.Window.CloseAsync or the test's end closed it): its page can no longer be driven."
        : "The app's page is not open: its window is starting.");

    /// <summary>Calls a DevTools Protocol method on the page's session; completes with the result object as JSON.</summary>
    private Task<string> CallDevToolsAsync(string method, string parametersJson) => OnUiAsync(() =>
    {
        var webView = WebViewOrThrow();
        var call = new DevToolsCall(method);
        webView.CallDevToolsProtocolMethod(method, parametersJson, call);
        return call.Task;
    });

    /// <summary>
    /// Receives a DevTools Protocol event of the page's session until the returned object is disposed. The callback
    /// runs on the UI thread: it must only copy the JSON out.
    /// </summary>
    private Task<IDisposable> SubscribeAsync(string eventName, Action<string> onEvent) => OnUiAsync(() =>
    {
        var webView = WebViewOrThrow();
        var receiver = Com.Take<ICoreWebView2DevToolsProtocolEventReceiver>(webView.GetDevToolsProtocolEventReceiver(eventName));
        var handler = new DevToolsEvents(onEvent, exception => _log.Error($"A handler of {eventName} failed", exception));
        var token = receiver.AddDevToolsProtocolEventReceived(handler);
        return Task.FromResult<IDisposable>(new Subscription(_process.Ui, receiver, handler, token));
    });

    public Task<IReadOnlyList<int>> RendererProcessesAsync() =>
        OnUiAsync(() => _window.View?.RendererProcessesAsync() ?? Task.FromResult<IReadOnlyList<int>>([]));

    public Task PostCloseMessageAsync() => OnUiAsync(() =>
    {
        PInvoke.PostMessage(_window.Handle, PInvoke.WM_CLOSE, default, default);
        return Task.CompletedTask;
    });

    public Task<SparTestCloseReport> RequestCloseAsync(SparTestClose close) => OnUiAsync(async () =>
    {
        var questions = close.CloseAnyway is { } closeAnyway ? new TestAnswers(closeAnyway) : null;
        var report = await _window.RequestCloseAsync(close.Reason, close.Automated, questions);
        return new SparTestCloseReport(report.Closed, report.Question, report.Unanswered);
    });

    public Task<int> CloseAsync()
    {
        _process.Ui.Post(() => _ = _window.TeardownAsync(0), "test window close");
        return _closed.Task;
    }

    public unsafe Task<(bool DarkTitleBar, uint WindowColour)> NativeThemeAsync() => OnUiAsync(() =>
    {
        BOOL dark = false;
        var read = PInvoke.DwmGetWindowAttribute(_window.Handle, global::Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, (uint)sizeof(BOOL));
        read.ThrowOnFailure();
        return Task.FromResult(((bool)dark, _window.WindowColour));
    });

    public Task<nint> SendMessageAsync(uint message, nint wParam, nint lParam) => OnUiAsync(() =>
    {
        var result = PInvoke.SendMessage(_window.Handle, message, new WPARAM((nuint)wParam), new LPARAM(lParam));
        return Task.FromResult((nint)result.Value);
    });

    /// <summary><c>WM_QUERYENDSESSION</c> then <c>WM_ENDSESSION</c> with <c>TRUE</c>, both for a log-off, as Windows ends a session.</summary>
    public async Task<bool> EndSessionAsync()
    {
        const uint QueryEndSession = 0x0011;
        const uint EndSession = 0x0016;
        var logoff = unchecked((nint)0x80000000);
        var allowed = await SendMessageAsync(QueryEndSession, 0, logoff);
        await SendMessageAsync(EndSession, 1, logoff);
        return allowed != 0;
    }

    void IAppWindowOwner.TeardownStarted() => _ = NoteRenderersAsync();

    /// <summary>The window is gone: the rest of its stop and the leftover-process check run off the UI thread.</summary>
    void IAppWindowOwner.WindowDestroyed()
    {
        _opened.TrySetException(new InvalidOperationException(
            $"The app's window closed before its page loaded (exit code {_window.ExitCode}); its log is in the test's logs folder."));
        _ = Task.Run(FinishAsync);
    }

    /// <summary>At session end the app's services are disposed inside the window's hold, as an app's are; the test host's later disposal finds them done.</summary>
    Task? IAppWindowOwner.SessionEnded() => Task.Run(async () =>
    {
        try
        {
            await (await _running).DisposeAsync();
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly at session end", exception);
        }
    });

    /// <summary>A test's session end ends no process: the test host closes the app's log file with the app.</summary>
    void IAppWindowOwner.SessionEndLogged(TimeSpan limit)
    {
    }

    private void Start()
    {
        try
        {
            _window.Start(_process.Ui, _theme, _running);
        }
        catch (Exception exception)
        {
            _opened.TrySetException(exception);
            _closed.TrySetException(exception);
            _process.ReturnProfile(_profile);
        }
    }

    /// <summary>
    /// Before the first navigation: the page's colour scheme, when the test fixed one, then the page driver. The
    /// profile's <c>PreferredColorScheme</c> did not reach <c>prefers-color-scheme</c> on a test's InPrivate profile
    /// (pitfalls), so the scheme is also emulated on the page's DevTools session, where it lasts across navigations.
    /// </summary>
    private async Task BeforeFirstNavigationAsync()
    {
        var automation = _request.Automation;
        if (_request.Dark is { } dark)
        {
            await CallDevToolsAsync("Emulation.setEmulatedMedia", MediaFeatures(dark ? "dark" : "light"));
        }
        else if (automation.ReducedMotion)
        {
            await CallDevToolsAsync("Emulation.setEmulatedMedia", MediaFeatures(null));
        }

        if (automation.Viewport is { } viewport)
        {
            // A scene's page: its size in CSS pixels at a device scale of 1, whatever the monitor's scale.
            await CallDevToolsAsync(
                "Emulation.setDeviceMetricsOverride",
                $$"""{"width":{{viewport.Width}},"height":{{viewport.Height}},"deviceScaleFactor":1,"mobile":false}""");
        }

        if (automation.PageScript is { } script)
        {
            // In the page's own world (no worldName), before the page's scripts, at the start of every document.
            await CallDevToolsAsync("Page.addScriptToEvaluateOnNewDocument", JsonSource(script));
        }

        _page = await TestPage.InstallAsync(CallDevToolsAsync, SubscribeAsync, automation);
    }

    /// <summary>
    /// The page's own <c>setColorScheme</c> in a test window: the emulated scheme follows the window's scheme after
    /// the choice (the system's for <c>system</c>), so the page's <c>prefers-color-scheme</c> agrees with the window.
    /// </summary>
    private async Task EmulateSchemeAsync(bool dark) =>
        await CallDevToolsAsync("Emulation.setEmulatedMedia", MediaFeatures(dark ? "dark" : "light"));

    /// <summary>
    /// The emulated media features, all in one call (each call replaces the last): the colour scheme when one is given,
    /// and a scene's <c>prefers-reduced-motion: reduce</c>.
    /// </summary>
    private string MediaFeatures(string? scheme)
    {
        var features = new List<string>(2);
        if (scheme is not null)
        {
            features.Add($$"""{"name":"prefers-color-scheme","value":"{{scheme}}"}""");
        }

        if (_request.Automation.ReducedMotion)
        {
            features.Add("""{"name":"prefers-reduced-motion","value":"reduce"}""");
        }

        return $$"""{"features":[{{string.Join(',', features)}}]}""";
    }

    private static string JsonSource(string script)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("source", script);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void OnPhase(string phase)
    {
        _request.OnPhase?.Invoke(phase);
        switch (phase)
        {
            case "first navigation completed":
                _opened.TrySetResult();
                _ = NoteRenderersAsync();
                break;
            case "navigation failed":
                _opened.TrySetException(new InvalidOperationException("The app's page failed to load; its log is in the test's logs folder."));
                break;
        }
    }

    /// <summary>Remembers the renderer processes that serve the page now, for the check after the window is gone.</summary>
    private async Task NoteRenderersAsync()
    {
        try
        {
            if (_window.View is not { } view)
            {
                return;
            }

            foreach (var process in await view.RendererProcessesAsync())
            {
                lock (_renderers)
                {
                    _renderers.Add(process);
                }
            }
        }
        catch (Exception exception)
        {
            // The view may be closing; what was noted before still counts.
            _log.Info($"The page's renderer processes could not be listed: {exception.Message}");
        }
    }

    private async Task FinishAsync()
    {
        try
        {
            await _window.StopAsync();
            await _hangHook.StopAsync(TimeSpan.FromSeconds(3));
            var leftovers = await LeftoverRenderersAsync();
            if (leftovers.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Renderer processes of the app's page were still running {RendererExitLimit.TotalSeconds:0} s after its window closed: {string.Join(", ", leftovers)}.");
            }

            _closed.TrySetResult(_window.ExitCode);
        }
        catch (Exception exception)
        {
            _closed.TrySetException(exception);
        }
        finally
        {
            _page?.Dispose();
            _process.ReturnProfile(_profile);
        }
    }

    /// <summary>The page's renderer processes that have not exited within the limit; a process id already gone counts as exited.</summary>
    private async Task<List<int>> LeftoverRenderersAsync()
    {
        int[] noted;
        lock (_renderers)
        {
            noted = [.. _renderers];
        }

        var limit = Stopwatch.GetTimestamp() + (long)(RendererExitLimit.TotalSeconds * Stopwatch.Frequency);
        var running = new List<int>();
        foreach (var id in noted)
        {
            using var process = TryOpen(id);
            if (process is null)
            {
                continue;
            }

            var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), limit);
            using var timeout = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                running.Add(id);
            }
        }

        return running;
    }

    private static Process? TryOpen(int id)
    {
        try
        {
            return Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private ICoreWebView2_22 WebViewOrThrow() =>
        _window.View?.WebView ?? throw new InvalidOperationException(_closed.Task.IsCompleted || _window.IsClosing
            ? "The app's window is closed (app.Window.CloseAsync or the test's end closed it): its page can no longer be driven."
            : "The app's page is not open: its window is starting.");

    /// <summary>Runs <paramref name="work"/> on the UI thread; its task's result and failures reach the caller off it.</summary>
    private Task<T> OnUiAsync<T>(Func<Task<T>> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process.Ui.Post(() => _ = RelayAsync(work, result), "test window call");
        return result.Task;
    }

    private Task OnUiAsync(Func<Task> work) => OnUiAsync(async () =>
    {
        await work();
        return true;
    });

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

    /// <summary>A window test's answer in place of every native dialog of its close request.</summary>
    private sealed class TestAnswers(bool closeAnyway) : ICloseQuestions
    {
        public Task<bool> ConfirmAsync(string question) => Task.FromResult(closeAnyway);

        public Task<bool> CloseUnansweredAsync() => Task.FromResult(closeAnyway);
    }

    /// <summary>A DevTools event subscription; disposing it removes the handler on the UI thread.</summary>
    private sealed class Subscription(
        UiThread ui,
        ICoreWebView2DevToolsProtocolEventReceiver receiver,
        DevToolsEvents handler,
        EventRegistrationToken token) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            ui.Post(() =>
            {
                try
                {
                    receiver.RemoveDevToolsProtocolEventReceived(token);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // A closed WebView refuses removals; its receiver is released anyway.
                }

                GC.KeepAlive(handler);
                Com.Release(receiver);
            }, "test window unsubscribe");
        }
    }
}
