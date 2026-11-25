using Spar.Devtools;
using Spar.Hosting;
using Spar.Windows.Interop;
using Spar.Windows.Testing;

namespace Spar.Windows.Devtools;

/// <summary>
/// The app window's page for the devtools of a Development build: the window tests' page automation (<see cref="TestPage"/>,
/// the engine in an isolated world of every document, over the WebView's in-process DevTools session) installed before
/// the first navigation, with the browser's own trusted input (decision "M2a's decide-first questions", item 4). Script
/// dialogs stay the developer's: the automation does not answer them. Calls from the devtools' threads are posted to the
/// UI thread, where each does only its DevTools call.
/// </summary>
internal sealed class DevtoolsPage : IDevtoolsPage, ISparTestPageEvents
{
    private readonly UiThread _ui;
    private readonly BrowserView _view;
    private readonly HostLog _log;
    private readonly DevtoolsLog _devtoolsLog;
    private TestPage? _page;
    private long _document;

    private DevtoolsPage(UiThread ui, BrowserView view, HostLog log, DevtoolsLog devtoolsLog)
    {
        _ui = ui;
        _view = view;
        _log = log;
        _devtoolsLog = devtoolsLog;
    }

    public bool TrustedInput => true;

    public long Document => Interlocked.Read(ref _document);

    /// <summary>On the UI thread, before the first navigation; the page is in <see cref="DevtoolsHost.Pages"/> after it.</summary>
    public static async Task InstallAsync(UiThread ui, BrowserView view, HostLog log, DevtoolsHost host, string engine)
    {
        var page = new DevtoolsPage(ui, view, log, host.Log);
        page._page = await TestPage.InstallAsync(
            page.CallOnUiAsync, page.SubscribeOnUiAsync, new SparTestAutomation(engine, DevtoolsHost.World, page), answerDialogs: false);
        host.Pages.Current = page;
    }

    public Task<string?> EngineAsync(string request, TimeSpan wait) => Page.EngineAsync(request, wait);

    public Task<byte[]> ScreenshotAsync() => Page.ScreenshotAsync(null);

    public async Task ClickAsync(double x, double y)
    {
        var page = Page;
        var (atX, atY) = ((float)x, (float)y);
        await page.PointerAsync(new SparTestPointer(SparTestPointerKind.Move, atX, atY));
        await page.PointerAsync(new SparTestPointer(SparTestPointerKind.Press, atX, atY, ClickCount: 1));
        await page.PointerAsync(new SparTestPointer(SparTestPointerKind.Release, atX, atY, ClickCount: 1));
    }

    public Task InsertTextAsync(string text) => Page.InsertTextAsync(text);

    void ISparTestPageEvents.Navigated(string url, bool newDocument)
    {
        if (newDocument)
        {
            Interlocked.Increment(ref _document);
        }
    }

    void ISparTestPageEvents.Console(string type, string text) => _devtoolsLog.PageConsole(type, text);

    void ISparTestPageEvents.PageError(string text) => _devtoolsLog.PageError(text);

    /// <summary>Not subscribed (<c>answerDialogs: false</c>); a dialog is left to the developer if one arrives anyway.</summary>
    bool ISparTestPageEvents.Dialog(string type, string message) => true;

    private TestPage Page => _page ?? throw new DevtoolsError("no_page", "The window's page automation is not installed yet; try again.");

    private Task<string> CallOnUiAsync(string method, string parameters) => OnUiAsync(() =>
    {
        var webView = _view.WebView ?? throw new DevtoolsError("no_page", "The window's WebView is gone (the window is closing).");
        var call = new DevToolsCall(method);
        webView.CallDevToolsProtocolMethod(method, parameters, call);
        return call.Task;
    });

    private Task<IDisposable> SubscribeOnUiAsync(string eventName, Action<string> onEvent) => OnUiAsync(() =>
    {
        var webView = _view.WebView ?? throw new DevtoolsError("no_page", "The window's WebView is gone (the window is closing).");
        var receiver = Com.Take<ICoreWebView2DevToolsProtocolEventReceiver>(webView.GetDevToolsProtocolEventReceiver(eventName));
        var handler = new DevToolsEvents(onEvent, exception => _log.Error($"A devtools handler of {eventName} failed", exception));
        var token = receiver.AddDevToolsProtocolEventReceived(handler);
        return Task.FromResult<IDisposable>(new Subscription(_ui, receiver, handler, token));
    });

    private Task<T> OnUiAsync<T>(Func<Task<T>> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(() => _ = RelayAsync(work, result), "devtools call");
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

    /// <summary>One event subscription; removed on the UI thread, where the WebView's receivers live.</summary>
    private sealed class Subscription(
        UiThread ui, ICoreWebView2DevToolsProtocolEventReceiver receiver, DevToolsEvents handler, EventRegistrationToken token) : IDisposable
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
            }, "devtools unsubscribe");
        }
    }
}
