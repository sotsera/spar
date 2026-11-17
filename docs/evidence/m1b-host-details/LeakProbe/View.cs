using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Spar.Windows.Interop;

namespace LeakProbe;

/// <summary>One window with its controller and page, opened and torn down on the probe's STA.</summary>
internal sealed unsafe class View
{
    private readonly List<(WebView2EventHandler Handler, EventRegistrationToken Token, Action<EventRegistrationToken> Remove)> _events = [];
    private readonly ICoreWebView2Environment10 _environment;
    private readonly nint _window;
    private readonly int _port;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2_22? _webView;
    private int _completed;

    private View(ICoreWebView2Environment10 environment, nint window, int port)
    {
        _environment = environment;
        _window = window;
        _port = port;
    }

    public bool Loaded { get; private set; }

    public double LoadMilliseconds { get; private set; }

    public static View Open(ICoreWebView2Environment10 environment, string mode, int port, int cycle)
    {
        var view = new View(environment, Program.CreateWindow(mode, cycle), port);
        view.Load();
        return view;
    }

    /// <summary>The host's order: events removed, the controller closed on the STA, the window destroyed.</summary>
    public string ClosePlain()
    {
        var started = Stopwatch.GetTimestamp();
        Release();
        return $"plain close {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms";
    }

    /// <summary>The earlier mitigation: hidden, about:blank, then the close posted outside the WebView2 callbacks.</summary>
    public string CloseMitigated()
    {
        var started = Stopwatch.GetTimestamp();
        _controller!.PutIsVisible(0);
        var before = _completed;
        _webView!.Navigate("about:blank");
        Program.Pump(() => _completed > before, 3_000);
        var blank = _completed > before;
        var done = false;
        Program.Post(_window, () =>
        {
            Release();
            done = true;
        });
        Program.Pump(() => done, 3_000);
        return $"mitigated close {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms (about:blank {(blank ? "loaded" : "not seen")}, closed {done})";
    }

    private void Load()
    {
        var started = Stopwatch.GetTimestamp();
        var created = new ControllerCompleted();
        _environment.CreateCoreWebView2Controller(_window, created);
        _controller = Com.Take<ICoreWebView2Controller>(Program.Wait(created.Task, 30_000));
        Program.GetClientRect(_window, out var client);
        _controller.PutBounds(client);
        _webView = Com.Take<ICoreWebView2_22>(_controller.GetCoreWebView2());
        _webView.AddWebResourceRequestedFilterWithRequestSourceKinds("https://app.localhost/*", WebView2Values.ContextAll, WebView2Values.SourceKindsAll);

        var webView = _webView;
        Subscribe(new WebView2EventHandler(Serve, Report), handler => webView.AddWebResourceRequested(handler), webView.RemoveWebResourceRequested);
        Subscribe(new WebView2EventHandler(_ => Loaded = true, Report), handler => webView.AddWebMessageReceived(handler), webView.RemoveWebMessageReceived);
        Subscribe(new WebView2EventHandler(_ => _completed++, Report), handler => webView.AddNavigationCompleted(handler), webView.RemoveNavigationCompleted);

        _webView.Navigate("https://app.localhost/index.html");
        Program.Pump(() => Loaded, 15_000);
        LoadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void Subscribe(WebView2EventHandler handler, Func<WebView2EventHandler, EventRegistrationToken> add, Action<EventRegistrationToken> remove) =>
        _events.Add((handler, add(handler), remove));

    private void Release()
    {
        foreach (var (_, token, remove) in _events)
        {
            remove(token);
        }

        _events.Clear();
        _controller?.Close();
        Com.Release(_webView);
        Com.Release(_controller);
        _webView = null;
        _controller = null;
        Program.DestroyWindow(_window);
    }

    /// <summary>The page and its data from memory, as the host serves its frontend.</summary>
    private void Serve(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2WebResourceRequestedEventArgs2>(argsPointer);
        var request = Com.Take<ICoreWebView2WebResourceRequest>(args.GetRequest());
        try
        {
            var uri = request.GetUri();
            var page = uri.Contains("/index.html", StringComparison.Ordinal);
            var body = page ? Page() : new string('d', 8192);
            var bytes = Encoding.UTF8.GetBytes(body);
            nint stream;
            fixed (byte* data = bytes)
            {
                stream = Native.CreateMemoryStream(data, (uint)bytes.Length);
            }

            var headers = page ? "Content-Type: text/html; charset=utf-8" : "Content-Type: text/plain";
            var response = _environment.CreateWebResourceResponse(stream, 200, "OK", headers + "\r\nCache-Control: no-store");
            args.PutResponse(response);
            Marshal.Release(response);
            Marshal.Release(stream);
        }
        finally
        {
            Com.Release(request);
            Com.Release(args);
        }
    }

    private string Page() =>
        "<!doctype html><title>leak</title><p>probe</p><script>(async () => {" +
        "for (let i = 0; i < 5; i++) { await (await fetch('/data?' + i)).text(); }" +
        (_port > 0 ? $"for (let i = 0; i < 5; i++) {{ await (await fetch('http://127.0.0.1:{_port}/x' + i)).text(); }}" : "") +
        "chrome.webview.postMessage('ready'); })();</script>";

    private static void Report(Exception exception) => Program.Note($"handler failed: {exception.Message}");
}
