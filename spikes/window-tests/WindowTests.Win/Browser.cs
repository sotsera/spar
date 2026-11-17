using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Spar.WindowTests.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.WindowTests;

/// <summary>One WebView2 environment on one user-data folder.</summary>
internal sealed class Env
{
    private BrowserProcessExitedHandler? _exitedHandler;

    private Env(string path, ICoreWebView2Environment10 environment)
    {
        Path = path;
        Environment = environment;
    }

    public string Path { get; }

    public ICoreWebView2Environment10 Environment { get; }

    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<Env> CreateAsync(string path, string browserArguments = "")
    {
        Directory.CreateDirectory(path);
        var options = new EnvironmentOptions { AdditionalBrowserArguments = browserArguments };
        var handler = new EnvironmentCompletedHandler();
        var hr = NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, path, options, handler);
        if (hr < 0)
        {
            throw new ComFailureException("CreateCoreWebView2EnvironmentWithOptions", hr);
        }

        var env = new Env(path, await handler.Task);
        env._exitedHandler = new BrowserProcessExitedHandler(() => env.Exited.TrySetResult());
        env.Environment.AddBrowserProcessExited(env._exitedHandler);
        return env;
    }
}

/// <summary>
/// One offscreen, never-activated window with one controller. Serves the spike's pages on <c>https://app.localhost</c>
/// and two other origins through <c>WebResourceRequested</c>, and drives the page through in-process CDP.
/// </summary>
internal sealed class View
{
    public static readonly string[] ServedOrigins = ["https://app.localhost", "https://other.invalid", "https://frame.invalid"];

    private static readonly string[] s_clickSequence = ["mouseMoved", "mousePressed", "mouseReleased"];

    private readonly HWND _window;
    private readonly List<object> _keepAlive = [];
    private TaskCompletionSource? _navigation;

    private View(Env env, HWND window, string profile)
    {
        Env = env;
        _window = window;
        Profile = profile;
    }

    public Env Env { get; }

    public string Profile { get; }

    public ICoreWebView2Controller? Controller { get; private set; }

    public ICoreWebView2_22 WebView { get; private set; } = null!;

    /// <summary>Extra response headers per path ("/file.html" => "Permissions-Policy: ...").</summary>
    public Dictionary<string, string> ExtraHeaders { get; } = [];

    public unsafe nint WindowHandle => (nint)_window.Value;

    /// <summary>
    /// Creates the window and the controller. <paramref name="profile"/> is "default", a ProfileName, "~private" or
    /// "~private:NAME". <paramref name="beforeFirstUse"/> runs right after the controller exists, before any filter or
    /// navigation (used to set up CDP state as early as possible).
    /// </summary>
    public static async Task<View> CreateAsync(Env env, string profile, bool devTools = true, Func<View, Task>? beforeFirstUse = null)
    {
        var window = Ui.CreateTestWindow("Window tests spike");
        var view = new View(env, window, profile);
        try
        {
            var handler = new ControllerCompletedHandler();
            if (profile == "default")
            {
                env.Environment.CreateCoreWebView2Controller(view.WindowHandle, handler);
            }
            else
            {
                var options = env.Environment.CreateCoreWebView2ControllerOptions();
                if (profile.StartsWith("~private", StringComparison.Ordinal))
                {
                    options.PutIsInPrivateModeEnabled(1);
                    if (profile.Length > 9)
                    {
                        options.PutProfileName(profile[9..]);
                    }
                }
                else
                {
                    options.PutProfileName(profile);
                }

                env.Environment.CreateCoreWebView2ControllerWithOptions(view.WindowHandle, options, handler);
            }

            view.Controller = await handler.Task;
            view.WebView = view.Controller.GetCoreWebView2();
            var settings = view.WebView.GetSettings();
            settings.PutIsReputationCheckingRequired(0);
            settings.PutAreDefaultScriptDialogsEnabled(0);
            if (!devTools)
            {
                settings.PutAreDevToolsEnabled(0);
            }

            PInvoke.GetClientRect(window, out var client);
            view.Controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
            if (beforeFirstUse is not null)
            {
                await beforeFirstUse(view);
            }

            var navigationHandler = new NavigationCompletedHandler(() => view._navigation?.TrySetResult());
            view.WebView.AddNavigationCompleted(navigationHandler);
            var resourceHandler = new WebResourceRequestedHandler(view.OnWebResourceRequested);
            view.WebView.AddWebResourceRequested(resourceHandler);
            foreach (var origin in ServedOrigins)
            {
                // Source-kinds filter: also catches service-worker and shared-worker requests (spikes/desktop-origin).
                view.WebView.AddWebResourceRequestedFilterWithRequestSourceKinds(origin + "/*", 0, unchecked((int)0xFFFFFFFF));
            }

            view._keepAlive.Add(navigationHandler);
            view._keepAlive.Add(resourceHandler);
            return view;
        }
        catch
        {
            view.Close();
            throw;
        }
    }

    public ICoreWebView2Profile8 GetProfile() => WebView.GetProfile();

    public async Task NavigateAsync(string url, int timeoutMilliseconds = 30000)
    {
        _navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebView.Navigate(url);
        await _navigation.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    public async Task ReloadAsync()
    {
        _navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebView.Reload();
        await _navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public Task<string> CallAsync(string method, string parametersJson = "{}")
    {
        var handler = new DevToolsCallHandler(method);
        WebView.CallDevToolsProtocolMethod(method, parametersJson, handler);
        return handler.Task;
    }

    public Task<string> CallForSessionAsync(string sessionId, string method, string parametersJson = "{}")
    {
        var handler = new DevToolsCallHandler(method);
        WebView.CallDevToolsProtocolMethodForSession(sessionId, method, parametersJson, handler);
        return handler.Task;
    }

    /// <summary>Subscribes to a CDP event; the callback gets the parameters JSON and the session id ("" for the page).</summary>
    public void Subscribe(string eventName, Action<string, string> onEvent)
    {
        var receiver = WebView.GetDevToolsProtocolEventReceiver(eventName);
        var handler = new DevToolsEventHandler(onEvent);
        receiver.AddDevToolsProtocolEventReceived(handler);
        _keepAlive.Add(receiver);
        _keepAlive.Add(handler);
    }

    public void OnPermissionRequested(Action<ICoreWebView2PermissionRequestedEventArgs3> onRequested)
    {
        var handler = new PermissionRequestedHandler(onRequested);
        WebView.AddPermissionRequested(handler);
        _keepAlive.Add(handler);
    }

    public void OnNewWindowRequested(Action<ICoreWebView2NewWindowRequestedEventArgs> onRequested)
    {
        var handler = new NewWindowRequestedHandler(onRequested);
        WebView.AddNewWindowRequested(handler);
        _keepAlive.Add(handler);
    }

    /// <summary>Evaluates an expression and returns its JSON value (<c>returnByValue</c>); throws on a page exception.</summary>
    public async Task<JsonElement> EvaluateAsync(string expression, bool awaitPromise = true, bool userGesture = false)
    {
        var json = await CallAsync("Runtime.evaluate", Params(w =>
        {
            w.WriteString("expression", expression);
            w.WriteBoolean("returnByValue", true);
            w.WriteBoolean("awaitPromise", awaitPromise);
            w.WriteBoolean("userGesture", userGesture);
        }));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("exceptionDetails", out var exception))
        {
            throw new InvalidOperationException("Page exception: " + exception.GetRawText());
        }

        return root.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    public async Task<string> EvaluateTextAsync(string expression, bool awaitPromise = true, bool userGesture = false)
    {
        var value = await EvaluateAsync(expression, awaitPromise, userGesture);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ValueKind == JsonValueKind.Undefined ? "undefined" : value.GetRawText();
    }

    /// <summary>The centre of the element matched by <paramref name="selector"/>, in CSS pixels of the main frame.</summary>
    public async Task<(double X, double Y)> CentreAsync(string selector, string? frameSelector = null)
    {
        var script = frameSelector is null
            ? $"(() => {{ const r = document.querySelector({JsonString(selector)}).getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }})()"
            : $"(() => {{ const f = document.querySelector({JsonString(frameSelector)}).getBoundingClientRect(); return [f.x + f.width / 2, f.y + f.height / 2]; }})()";
        var value = await EvaluateAsync(script, awaitPromise: false);
        return (value[0].GetDouble(), value[1].GetDouble());
    }

    /// <summary>A trusted click: mouseMoved, mousePressed, mouseReleased through <c>Input.dispatchMouseEvent</c>.</summary>
    public async Task ClickAtAsync(double x, double y)
    {
        foreach (var type in s_clickSequence)
        {
            await CallAsync("Input.dispatchMouseEvent", Params(w =>
            {
                w.WriteString("type", type);
                w.WriteNumber("x", x);
                w.WriteNumber("y", y);
                if (type != "mouseMoved")
                {
                    w.WriteString("button", "left");
                    w.WriteNumber("clickCount", 1);
                }
            }));
        }
    }

    public async Task ClickAsync(string selector)
    {
        var (x, y) = await CentreAsync(selector);
        await ClickAtAsync(x, y);
    }

    public async Task KeyAsync(string key, string code, int virtualKey, string? text = null)
    {
        await CallAsync("Input.dispatchKeyEvent", Params(w =>
        {
            w.WriteString("type", text is null ? "rawKeyDown" : "keyDown");
            w.WriteString("key", key);
            w.WriteString("code", code);
            w.WriteNumber("windowsVirtualKeyCode", virtualKey);
            if (text is not null)
            {
                w.WriteString("text", text);
            }
        }));
        await CallAsync("Input.dispatchKeyEvent", Params(w =>
        {
            w.WriteString("type", "keyUp");
            w.WriteString("key", key);
            w.WriteString("code", code);
            w.WriteNumber("windowsVirtualKeyCode", virtualKey);
        }));
    }

    public Task InsertTextAsync(string text) => CallAsync("Input.insertText", Params(w => w.WriteString("text", text)));

    /// <summary>Waits for two animation frames in the page (the d8-automation readiness rule).</summary>
    public Task<JsonElement> TwoFramesAsync() =>
        EvaluateAsync("new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(performance.now()))))");

    public void Close()
    {
        if (Controller is not null)
        {
            Controller.Close();
            Controller = null;
        }

        if (!_window.IsNull)
        {
            PInvoke.DestroyWindow(_window);
        }

        _keepAlive.Clear();
    }

    public static string Params(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // A JSON string literal is also a valid JavaScript string literal.
    public static string JsonString(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var uri = new Uri(args.GetRequest().GetUri());
        var path = uri.AbsolutePath == "/" ? "/index.html" : uri.AbsolutePath;
        var content = Pages.Get(path);
        if (content is null)
        {
            args.PutResponse(Respond([], 404, "Not Found", "text/plain"));
            return;
        }

        var type = path.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript; charset=utf-8" : "text/html; charset=utf-8";
        var extra = path == "/sw.js" ? "\r\nService-Worker-Allowed: /" : "";
        if (ExtraHeaders.TryGetValue(path, out var header))
        {
            extra += "\r\n" + header;
        }

        args.PutResponse(Respond(content, 200, "OK", type, extra));
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType, string extraHeaders = "")
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        try
        {
            return Env.Environment.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: no-store{extraHeaders}");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }
}

/// <summary>The spike's pages, embedded from <c>www/</c>.</summary>
internal static class Pages
{
    private static readonly Dictionary<string, byte[]?> s_cache = [];

    public static byte[]? Get(string path)
    {
        lock (s_cache)
        {
            if (!s_cache.TryGetValue(path, out var content))
            {
                using var stream = typeof(Pages).Assembly.GetManifestResourceStream("www" + path);
                if (stream is not null)
                {
                    content = new byte[stream.Length];
                    stream.ReadExactly(content);
                }

                s_cache[path] = content;
            }

            return content;
        }
    }
}

internal static class Time
{
    public static double Ms(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
