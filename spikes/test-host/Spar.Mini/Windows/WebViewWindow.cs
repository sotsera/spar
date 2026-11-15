using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using React.Desktop.Interop;

namespace Spar.Windows;

/// <summary>
/// One Win32 window with a WebView2 controller, serving the packaged page on https://app.localhost and the bridge
/// over web messages. Everything here runs on the window's UI thread; commands run on the thread pool.
/// </summary>
[SupportedOSPlatform("windows10.0")]
internal sealed class WebViewWindow : ISparTestWindow
{
    public const string Origin = "https://app.localhost";
    private const string ClassName = "SparTestHostWindow";
    private static ushort s_classAtom;

    private readonly SparRuntime _runtime;
    private readonly UiThread _ui;
    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GCHandle _self;
    private nint _window;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _view;
    private int _browserProcessId;

    private WebViewWindow(SparRuntime runtime, UiThread ui)
    {
        _runtime = runtime;
        _ui = ui;
    }

    public IReadOnlyList<int> EngineProcessIds => _browserProcessId == 0 ? [] : [_browserProcessId];

    /// <summary>Completes when the window is destroyed (the user closed it, or teardown ran).</summary>
    public Task Destroyed => _destroyed.Task;

    public static Task<ISparTestWindow> OpenForTestAsync(SparRuntime runtime, CancellationToken cancellationToken)
    {
        var ui = UiThread.Shared;
        return ui.InvokeAsync<ISparTestWindow>(async () =>
        {
            var window = new WebViewWindow(runtime, ui);
            await window.CreateAsync(offscreen: true);
            return window;
        }).WaitAsync(cancellationToken);
    }

    public static Task<WebViewWindow> OpenAsync(SparRuntime runtime, UiThread ui) => ui.InvokeAsync(async () =>
    {
        var window = new WebViewWindow(runtime, ui);
        await window.CreateAsync(offscreen: false);
        return window;
    });

    public async Task<string> EvaluateAsync(string asyncFunctionBody, CancellationToken cancellationToken)
    {
        var expression = $"(async () => JSON.stringify(await (async () => {{ {asyncFunctionBody} }})()))()";
        var parameters = JsonSerializer.Serialize(new EvaluateParameters(expression), MiniJson.Default.EvaluateParameters);
        var result = await _ui.InvokeAsync(() =>
        {
            var handler = new DevToolsCallHandler("Runtime.evaluate");
            View.CallDevToolsProtocolMethod("Runtime.evaluate", parameters, handler);
            return handler.Task;
        }).WaitAsync(cancellationToken);

        using var document = JsonDocument.Parse(result);
        if (document.RootElement.TryGetProperty("exceptionDetails", out var exception))
        {
            throw new InvalidOperationException($"The page threw: {exception}");
        }

        return document.RootElement.GetProperty("result").GetProperty("value").GetString() ?? "null";
    }

    public async ValueTask DisposeAsync()
    {
        var clock = Stopwatch.StartNew();
        await _ui.InvokeAsync(Teardown);
        var teardown = clock.Elapsed.TotalMilliseconds;
        await WaitForBrowserExitAsync();
        _runtime.Log.Write("spike", $"teardown={teardown:F0}ms browser-exit={clock.Elapsed.TotalMilliseconds - teardown:F0}ms");
        _closing.Dispose();
    }

    /// <summary>Closes the controller and destroys the window, on the UI thread.</summary>
    internal void Teardown()
    {
        _closing.Cancel();
        _controller?.Close();
        var release = Environment.GetEnvironmentVariable("SPIKE_RELEASE") ?? "final";
        if (release is "final" or "final+gc")
        {
            // The managed wrappers hold COM references until the GC runs them down; the browser process stays
            // until every reference to its environment is gone. Release them now.
            Release(_view);
            Release(_controller);
            Release(_environment);
        }

        _controller = null;
        _view = null;
        _environment = null;
        if (_window != 0)
        {
            Win32.DestroyWindow(_window);
        }

        if (release is "gc" or "final+gc")
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void Release(object? wrapper)
    {
        if (wrapper is System.Runtime.InteropServices.Marshalling.ComObject com)
        {
            com.FinalRelease();
        }
    }

    internal async Task WaitForBrowserExitAsync()
    {
        if (_browserProcessId == 0)
        {
            return;
        }

        try
        {
            using var browser = Process.GetProcessById(_browserProcessId);
            await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private ICoreWebView2 View => _view ?? throw new ObjectDisposedException(nameof(WebViewWindow));

    private async Task CreateAsync(bool offscreen)
    {
        EnsureClass();
        var options = _runtime.Window;
        var (x, y) = offscreen ? (-30000, -30000) : (unchecked((int)0x80000000), unchecked((int)0x80000000));
        // Test windows: no taskbar button and no activation, so a test run never takes the user's focus.
        var exStyle = offscreen && Environment.GetEnvironmentVariable("SPIKE_EXSTYLE") != "0" ? Win32.WsExToolWindow | Win32.WsExNoActivate : 0u;
        _window = Win32.CreateWindowExW(exStyle, ClassName, options.Title, Win32.WsOverlappedWindow, x, y, options.Width, options.Height, 0, 0, Win32.GetModuleHandleW(0), 0);
        if (_window == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }

        _self = GCHandle.Alloc(this);
        Win32.SetWindowLongPtrW(_window, Win32.GwlpUserData, GCHandle.ToIntPtr(_self));

        // Tests show the window without activating it, offscreen: never hidden, which does not render.
        Win32.ShowWindow(_window, offscreen ? Win32.SwShowNoActivate : Win32.SwShowNormal);

        Directory.CreateDirectory(_runtime.Folders.WebView);
        var environmentCreated = new EnvironmentCompletedHandler();
        Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, _runtime.Folders.WebView, 0, environmentCreated));
        _environment = await environmentCreated.Task;

        var controllerCreated = new ControllerCompletedHandler();
        _environment.CreateCoreWebView2Controller(_window, controllerCreated);
        _controller = await controllerCreated.Task;
        Resize();
        _controller.PutIsVisible(1);

        _view = _controller.GetCoreWebView2();
        _browserProcessId = (int)_view.GetBrowserProcessId();

        // Decided default (decision "SmartScreen default"): the page never leaves the app origin, and the check
        // otherwise holds the browser for about 6.4 s after an early close (spikes/webview-profiles).
        if (Environment.GetEnvironmentVariable("SPIKE_REPUTATION") != "1")
        {
            ((ICoreWebView2Settings8)(object)_view.GetSettings()).PutIsReputationCheckingRequired(0);
        }
        _view.AddWebResourceRequestedFilter(Origin + "/*", 0);
        _view.AddWebResourceRequested(new WebResourceRequestedHandler(OnResourceRequested));
        _view.AddWebMessageReceived(new WebMessageReceivedHandler(OnMessage));

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _view.AddNavigationCompleted(new NavigationCompletedHandler(() => loaded.TrySetResult()));
        _view.Navigate(Origin + "/index.html");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private unsafe void OnResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var path = new Uri(args.GetRequest().GetUri()).AbsolutePath;
        var found = _runtime.Assets.TryGet(path, out var content);
        var headers = found
            ? $"Content-Type: {SparAssets.ContentType(path == "/" ? "/index.html" : path)}\r\nContent-Security-Policy: {SparAssets.ContentSecurityPolicy}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store"
            : "Content-Type: text/plain\r\nCache-Control: no-store";
        var body = found ? content : Encoding.UTF8.GetBytes("Not found");

        nint stream;
        fixed (byte* bytes = body)
        {
            stream = NativeLoader.SHCreateMemStream(bytes, (uint)body.Length);
        }

        try
        {
            args.PutResponse(_environment!.CreateWebResourceResponse(stream, found ? 200 : 404, found ? "OK" : "Not Found", headers));
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private void OnMessage(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.GetSource().StartsWith(Origin + "/", StringComparison.Ordinal) || args.TryGetWebMessageAsString(out var message) < 0 || message is null)
        {
            return;
        }

        _ = AnswerAsync(message);
    }

    private async Task AnswerAsync(string message)
    {
        var token = _closing.Token;

        // The command runs on the thread pool; the reply is posted back from this (UI) thread.
        var reply = await Task.Run(() => _runtime.Bridge.HandleAsync(message, token), CancellationToken.None);
        if (!token.IsCancellationRequested)
        {
            _view?.PostWebMessageAsString(reply);
        }
    }

    private void Resize()
    {
        Win32.GetClientRect(_window, out var bounds);
        _controller?.PutBounds(bounds);
    }

    private static unsafe void EnsureClass()
    {
        if (s_classAtom != 0)
        {
            return;
        }

        fixed (char* name = ClassName)
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WindowProcedure = &WindowProcedure,
                Instance = Win32.GetModuleHandleW(0),
                ClassName = name,
            };
            s_classAtom = Win32.RegisterClassExW(&windowClass);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        var handle = Win32.GetWindowLongPtrW(window, Win32.GwlpUserData);
        if (handle != 0 && GCHandle.FromIntPtr(handle).Target is WebViewWindow self)
        {
            switch (message)
            {
                case Win32.WmSize:
                    self.Resize();
                    break;
                case Win32.WmDestroy:
                    Win32.SetWindowLongPtrW(window, Win32.GwlpUserData, 0);
                    self._window = 0;
                    self._self.Free();
                    self._destroyed.TrySetResult();
                    break;
            }
        }

        return Win32.DefWindowProcW(window, message, wParam, lParam);
    }
}

/// <summary>Production: one UI thread, one window, until the user closes it.</summary>
[SupportedOSPlatform("windows10.0")]
internal static class WindowsHost
{
    public static int RunUntilClosed(SparRuntime runtime)
    {
        var ui = UiThread.Start("Spar UI");
        var window = WebViewWindow.OpenAsync(runtime, ui).GetAwaiter().GetResult();
        window.Destroyed.GetAwaiter().GetResult();
        ui.InvokeAsync(window.Teardown).GetAwaiter().GetResult();
        window.WaitForBrowserExitAsync().GetAwaiter().GetResult();
        ui.Quit();
        ui.Thread.Join();
        return 0;
    }
}
