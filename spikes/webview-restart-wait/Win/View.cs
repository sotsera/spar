using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using RestartWait.Interop;

namespace RestartWait;

/// <summary>
/// One window with one controller and its page, served from memory on <c>https://app.localhost</c> through
/// <c>WebResourceRequested</c> as Spar's host serves its frontend. Everything runs on the probe's STA.
/// </summary>
internal sealed class View(ICoreWebView2Environment10 environment, Dictionary<string, string> options)
{
    private const string Page = "<!doctype html><title>restart</title><p>restart wait probe</p><script>fetch('/data').then(r => r.text());</script>";

    private readonly List<object> _keepAlive = [];
    private nint _window;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2_22? _webView;
    private bool _navigated;

    public uint BrowserProcessId { get; private set; }

    /// <summary>Creates the window and controller and waits for the first navigation; fills the cycle's times.</summary>
    public void Open(Cycle cycle)
    {
        var started = Native.Now;
        _window = Native.CreateTestWindow(options["--window"] != "hidden");
        var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
        switch (options["--profile"])
        {
            case "private":
                controllerOptions.PutIsInPrivateModeEnabled(1);
                break;
            case "named":
                controllerOptions.PutProfileName("restart");
                break;
        }

        var handler = new ControllerCompletedHandler();
        environment.CreateCoreWebView2ControllerWithOptions(_window, controllerOptions, handler);
        if (options.GetValueOrDefault("--sample") == "true")
        {
            // Which browser processes of the folder exist while the creation waits, every 250 ms.
            var marker = environment.GetUserDataFolder();
            while (!Native.Pump(() => handler.Task.IsCompleted, 250))
            {
                var browsers = Native.BrowserProcesses(marker).Where(p => p.Kind == "browser").Select(p => p.Id);
                cycle.Samples.Add($"+{Native.Now - started:0} ms browsers [{string.Join(" ", browsers)}]");
            }
        }

        _controller = Native.Wait(handler.Task, 60_000);
        cycle.ControllerMs = Native.Now - started;

        var navigationStarted = Native.Now;
        _webView = _controller.GetCoreWebView2();
        BrowserProcessId = _webView.GetBrowserProcessId();
        var settings = _webView.GetSettings();
        settings.PutIsReputationCheckingRequired(options["--smartscreen"] == "on" ? 1 : 0);
        Native.GetClientRect(_window, out var client);
        _controller.PutBounds(client);
        var navigation = new NavigationCompletedHandler(() => _navigated = true);
        var resources = new WebResourceRequestedHandler(Serve);
        _webView.AddNavigationCompleted(navigation);
        _webView.AddWebResourceRequested(resources);
        _webView.AddWebResourceRequestedFilterWithRequestSourceKinds("https://app.localhost/*", 0, unchecked((int)0xFFFFFFFF));
        _keepAlive.Add(navigation);
        _keepAlive.Add(resources);
        _webView.Navigate("https://app.localhost/index.html");
        if (!Native.Pump(() => _navigated, 60_000))
        {
            throw new TimeoutException("The first navigation did not complete in 60 s.");
        }

        cycle.NavigationMs = Native.Now - navigationStarted;
    }

    /// <summary>The host's order: the controller closed on the STA, then the window destroyed.</summary>
    public void Close()
    {
        if (_controller is not null)
        {
            _controller.Close();
            ((ComObject)(object)_webView!).FinalRelease();
            ((ComObject)(object)_controller).FinalRelease();
            _controller = null;
            _webView = null;
        }

        if (_window != 0)
        {
            Native.DestroyWindow(_window);
            _window = 0;
        }

        _keepAlive.Clear();
    }

    private unsafe void Serve(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var page = args.GetRequest().GetUri().EndsWith("/index.html", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(page ? Page : new string('d', 4096));
        nint stream;
        fixed (byte* data = bytes)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)bytes.Length);
        }

        try
        {
            var contentType = page ? "text/html; charset=utf-8" : "text/plain";
            args.PutResponse(environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {contentType}\r\nCache-Control: no-store"));
        }
        finally
        {
            Marshal.Release(stream);
        }
    }
}
