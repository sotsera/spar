using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Spar.WindowAutomation.Interop;

namespace Spar.WindowAutomation;

/// <summary>
/// One WebView2 environment per test process (decision "Window-test profiles, ..."), on its own root under
/// <c>artifacts/webview2-profiles/window-automation/</c>, with one anchor controller kept alive for the whole process:
/// without it the last test's close ends the browser and the next controller waits about 5.7 s (pitfall "The last
/// controller's close ends the browser"), and with a remote debugging port the port would change.
/// </summary>
internal sealed class Env
{
    private BrowserProcessExitedHandler? _exitedHandler;

    private Env(string path, ICoreWebView2Environment10 environment)
    {
        Path = path;
        Environment = environment;
    }

    public string Path { get; }

    /// <summary>The command-line fragment of this folder's browser processes.</summary>
    public string Marker => System.IO.Path.Combine(Path, "EBWebView");

    public ICoreWebView2Environment10 Environment { get; }

    public View? Anchor { get; private set; }

    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the environment and its anchor on the UI thread. <paramref name="remoteDebugging"/> adds <c>--remote-debugging-port=0</c>.</summary>
    public static Task<Env> CreateAsync(string path, bool remoteDebugging, string anchor = "default") => Ui.InvokeAsync(async () =>
    {
        Directory.CreateDirectory(path);
        var options = new EnvironmentOptions
        {
            // The spike's browser arguments: the File System Access APIs removed, as the host does (decision above).
            AdditionalBrowserArguments = "--disable-blink-features=FileSystemAccessLocal" + (remoteDebugging ? " --remote-debugging-port=0" : ""),
        };
        var handler = new EnvironmentCompletedHandler();
        var hr = NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, path, options, handler);
        if (hr < 0)
        {
            throw new ComFailureException("CreateCoreWebView2EnvironmentWithOptions", hr);
        }

        var env = new Env(path, await handler.Task);
        env._exitedHandler = new BrowserProcessExitedHandler(() => env.Exited.TrySetResult());
        env.Environment.AddBrowserProcessExited(env._exitedHandler);
        // The anchor: "private" (InPrivate), "default" (the folder's default profile) or "none".
        env.Anchor = anchor == "none" ? null : await View.CreateOnUiAsync(env, anchor: true, inPrivate: anchor == "private");
        return env;
    });

    public Task CloseAsync() => Ui.Invoke(() => Anchor?.CloseOnUi());

    /// <summary>The port the browser wrote to <c>EBWebView\DevToolsActivePort</c>, waiting for the file.</summary>
    public async Task<int> DevToolsPortAsync(TimeSpan timeout)
    {
        var file = System.IO.Path.Combine(Path, "EBWebView", "DevToolsActivePort");
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                if (File.Exists(file) && File.ReadAllLines(file) is [var port, ..] && int.TryParse(port, out var value))
                {
                    return value;
                }
            }
            catch (IOException)
            {
            }

            if (Stopwatch.GetElapsedTime(started) > timeout)
            {
                throw new TimeoutException("No DevToolsActivePort in " + file);
            }

            await Task.Delay(10);
        }
    }
}

/// <summary>
/// One offscreen, never-activated window with one InPrivate controller, serving the built page on
/// <c>https://app.localhost</c> through <c>WebResourceRequested</c> as Spar's host does, and making the host's own
/// in-process DevTools calls: <c>Page.enable</c> and <c>Page.setInterceptFileChooserDialog</c> before the first
/// navigation (Spar.Windows' PagePicker). Every WebView2 call runs on the UI thread.
/// </summary>
internal sealed class View
{
    public const string Origin = "https://app.localhost";

    private readonly List<object> _keepAlive = [];
    private nint _window;
    private TaskCompletionSource? _navigation;
    private int _fileChoosers;
    private int _served;

    private View(Env env) => Env = env;

    public Env Env { get; }

    public ICoreWebView2Controller? Controller { get; private set; }

    public ICoreWebView2_22 WebView { get; private set; } = null!;

    /// <summary>This page's DevTools target id, from the in-process <c>Target.getTargetInfo</c>.</summary>
    public string TargetId { get; private set; } = "";

    public uint BrowserProcessId { get; private set; }

    /// <summary><c>Page.fileChooserOpened</c> events the host's own session received.</summary>
    public int FileChoosersIntercepted => Volatile.Read(ref _fileChoosers);

    /// <summary>Requests answered by <c>WebResourceRequested</c>.</summary>
    public int Served => Volatile.Read(ref _served);

    /// <summary>The folder served on <see cref="Origin"/>.</summary>
    public static string PageFolder { get; set; } = "";

    public static Task<View> CreateAsync(Env env, bool interceptPicker = true, string? profileName = null) =>
        Ui.InvokeAsync(() => CreateOnUiAsync(env, anchor: false, interceptPicker, profileName: profileName));

    public static async Task<View> CreateOnUiAsync(Env env, bool anchor, bool interceptPicker = true, bool inPrivate = true, string? profileName = null)
    {
        var view = new View(env) { _window = Ui.CreateTestWindow() };
        try
        {
            var options = env.Environment.CreateCoreWebView2ControllerOptions();
            options.PutIsInPrivateModeEnabled(inPrivate ? 1 : 0);
            if (profileName is not null)
            {
                options.PutProfileName(profileName);
            }

            var handler = new ControllerCompletedHandler();
            env.Environment.CreateCoreWebView2ControllerWithOptions(view._window, options, handler);
            view.Controller = await handler.Task;
            view.WebView = view.Controller.GetCoreWebView2();
            view.BrowserProcessId = view.WebView.GetBrowserProcessId();
            var settings = view.WebView.GetSettings();
            settings.PutIsReputationCheckingRequired(0);
            Native.GetClientRect(view._window, out var client);
            view.Controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.Right, Bottom = client.Bottom });
            if (anchor)
            {
                return view;
            }

            if (interceptPicker)
            {
                // As Spar.Windows' PagePicker: Page.enable first, or the interception silently does nothing.
                view.Subscribe("Page.fileChooserOpened", _ => Interlocked.Increment(ref view._fileChoosers));
                await view.CallOnUiAsync("Page.enable");
                await view.CallOnUiAsync("Page.setInterceptFileChooserDialog", """{"enabled":true}""");
            }

            using (var info = JsonDocument.Parse(await view.CallOnUiAsync("Target.getTargetInfo")))
            {
                view.TargetId = info.RootElement.GetProperty("targetInfo").GetProperty("targetId").GetString()!;
            }

            var navigationHandler = new NavigationCompletedHandler(() => view._navigation?.TrySetResult());
            view.WebView.AddNavigationCompleted(navigationHandler);
            var resourceHandler = new WebResourceRequestedHandler(view.OnWebResourceRequested);
            view.WebView.AddWebResourceRequested(resourceHandler);
            view.WebView.AddWebResourceRequestedFilterWithRequestSourceKinds(Origin + "/*", 0, unchecked((int)0xFFFFFFFF));
            var newWindowHandler = new NewWindowRequestedHandler(args => args.PutHandled(1));
            view.WebView.AddNewWindowRequested(newWindowHandler);
            view._keepAlive.Add(navigationHandler);
            view._keepAlive.Add(resourceHandler);
            view._keepAlive.Add(newWindowHandler);
            return view;
        }
        catch
        {
            view.CloseOnUi();
            throw;
        }
    }

    /// <summary>Navigates and waits for <c>NavigationCompleted</c>.</summary>
    public Task NavigateAsync(string url) => Ui.InvokeAsync(async () =>
    {
        _navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebView.Navigate(url);
        await _navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
    });

    /// <summary>One in-process DevTools call, posted to the UI thread; completes off it.</summary>
    public Task<string> CallAsync(string method, string parametersJson = "{}") =>
        Ui.IsUiThread ? CallOnUiAsync(method, parametersJson) : Ui.InvokeAsync(() => CallOnUiAsync(method, parametersJson));

    /// <summary>Subscribes to a DevTools event of this page; the callback runs on the UI thread and must be short.</summary>
    public void Subscribe(string eventName, Action<string> onEvent)
    {
        var receiver = WebView.GetDevToolsProtocolEventReceiver(eventName);
        var handler = new DevToolsEventHandler(onEvent);
        receiver.AddDevToolsProtocolEventReceived(handler);
        _keepAlive.Add(receiver);
        _keepAlive.Add(handler);
    }

    public Task CloseAsync() => Ui.Invoke(CloseOnUi);

    public void CloseOnUi()
    {
        if (Controller is not null)
        {
            Controller.Close();
            Controller = null;
        }

        if (_window != 0)
        {
            Native.DestroyWindow(_window);
            _window = 0;
        }

        _keepAlive.Clear();
    }

    private Task<string> CallOnUiAsync(string method, string parametersJson = "{}")
    {
        var handler = new DevToolsCallHandler(method);
        WebView.CallDevToolsProtocolMethod(method, parametersJson, handler);
        return handler.Task;
    }

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        Interlocked.Increment(ref _served);
        var uri = new Uri(args.GetRequest().GetUri());
        var path = uri.AbsolutePath.TrimStart('/');

        // A client-side route (no extension) gets the page itself.
        if (path.Length == 0 || !System.IO.Path.HasExtension(path))
        {
            path = "index.html";
        }

        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(PageFolder, path));
        if (!file.StartsWith(PageFolder, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            args.PutResponse(Respond([], 404, "Not Found", "text/plain"));
            return;
        }

        args.PutResponse(Respond(File.ReadAllBytes(file), 200, "OK", ContentType(file)));
    }

    private static string ContentType(string file) => System.IO.Path.GetExtension(file) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType)
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        try
        {
            return Env.Environment.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: no-store");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }
}
