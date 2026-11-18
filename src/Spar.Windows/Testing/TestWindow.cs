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

    private TestWindow(TestWindowProcess process, SparApp app, SparRunningApp running, SparTestWindowRequest request)
    {
        _process = process;
        _request = request;
        _running = Task.FromResult(running);
        _profile = process.RentProfile();
        _log.UseAppLogging(running.Services);
        _hangHook = new PageHangHook(app.Builder.PageHangHandler, _log.Logger);
        _theme = WindowTheme.Read(app.Builder.MainWindow, running.Folders.DataRoot);

        var source = new BrowserSource
        {
            SharedEnvironment = process.EnvironmentAsync,
            PrivateProfile = _profile,
            BeforeFirstNavigation = BeforeFirstNavigationAsync,
            OnPhase = OnPhase,
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

    public Task<string> CallDevToolsAsync(string method, string parametersJson) => OnUiAsync(() =>
    {
        var webView = WebViewOrThrow();
        var call = new DevToolsCall(method);
        webView.CallDevToolsProtocolMethod(method, parametersJson, call);
        return call.Task;
    });

    public Task<IDisposable> SubscribeAsync(string eventName, Action<string> onEvent) => OnUiAsync(() =>
    {
        var webView = WebViewOrThrow();
        var receiver = Com.Take<ICoreWebView2DevToolsProtocolEventReceiver>(webView.GetDevToolsProtocolEventReceiver(eventName));
        var handler = new DevToolsEvents(onEvent, exception => _log.Error($"A handler of {eventName} failed", exception));
        var token = receiver.AddDevToolsProtocolEventReceived(handler);
        return Task.FromResult<IDisposable>(new Subscription(_process.Ui, receiver, handler, token));
    });

    public Task<IReadOnlyList<int>> RendererProcessesAsync() =>
        OnUiAsync(() => _window.View?.RendererProcessesAsync() ?? Task.FromResult<IReadOnlyList<int>>([]));

    public Task RequestCloseAsync() => OnUiAsync(() =>
    {
        PInvoke.PostMessage(_window.Handle, PInvoke.WM_CLOSE, default, default);
        return Task.CompletedTask;
    });

    public Task<int> CloseAsync()
    {
        _process.Ui.Post(() => _ = _window.TeardownAsync(0), "test window close");
        return _closed.Task;
    }

    public Task<nint> SendMessageAsync(uint message, nint wParam, nint lParam) => OnUiAsync(() =>
    {
        var result = PInvoke.SendMessage(_window.Handle, message, new WPARAM((nuint)wParam), new LPARAM(lParam));
        return Task.FromResult((nint)result.Value);
    });

    void IAppWindowOwner.TeardownStarted() => _ = NoteRenderersAsync();

    /// <summary>The window is gone: the rest of its stop and the leftover-process check run off the UI thread.</summary>
    void IAppWindowOwner.WindowDestroyed()
    {
        _opened.TrySetException(new InvalidOperationException(
            $"The app's window closed before its page loaded (exit code {_window.ExitCode}); its log is in the test's logs folder."));
        _ = Task.Run(FinishAsync);
    }

    Task? IAppWindowOwner.SessionEnded() => null;

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

    private Task BeforeFirstNavigationAsync() => _request.BeforeFirstNavigation?.Invoke(this) ?? Task.CompletedTask;

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
        _window.View?.WebView ?? throw new InvalidOperationException("The app's page is not open: its window is starting or closed.");

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
