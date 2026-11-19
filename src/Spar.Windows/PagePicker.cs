using System.Diagnostics;
using System.Runtime.InteropServices.Marshalling;
using Spar.Hosting;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// Keeps the engine's file picker closed for <c>&lt;input type="file"&gt;</c> (plan section 3c; decision "Window-test
/// profiles, trusted input, the page's pickers and the Sandbox runner"). WebView2 has no setting for it, so the host
/// makes exactly two in-process DevTools Protocol calls on each WebView before its first navigation: <c>Page.enable</c>,
/// without which the second call returns <c>{}</c> and changes nothing, then
/// <c>Page.setInterceptFileChooserDialog</c>. The browser then reports each picker as an event no one listens to, and
/// the page sees only its click: no files, no <c>change</c>. These calls open no endpoint and need no devtools
/// (<c>AreDevToolsEnabled</c> is off); they are the only DevTools Protocol use a Production build carries, and the
/// production boundary test checks that no other method name is in its image.
/// <para>
/// One completion handler makes the second call when the first completes, on the UI thread, so a Production image
/// carries no async machinery for it. A failure fails <see cref="Task"/>: the start fails as a broken Runtime (exit 5)
/// rather than run with the picker open.
/// </para>
/// </summary>
[GeneratedComClass]
internal sealed partial class PagePicker : ICoreWebView2CallDevToolsProtocolMethodCompletedHandler
{
    private readonly ICoreWebView2_22 _webView;
    private readonly HostLog _log;
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _started = Stopwatch.GetTimestamp();
    private double _enabledAfter = -1;

    /// <summary>Completes once both calls succeeded, with the time they took in the log.</summary>
    public Task Task => _done.Task;

    private PagePicker(ICoreWebView2_22 webView, HostLog log)
    {
        _webView = webView;
        _log = log;
    }

    /// <summary>Starts the two calls on the UI thread.</summary>
    public static Task Block(ICoreWebView2_22 webView, HostLog log)
    {
        var picker = new PagePicker(webView, log);
        webView.CallDevToolsProtocolMethod("Page.enable", "{}", picker);
        return picker.Task;
    }

    public void Invoke(int errorCode, string? returnObjectAsJson)
    {
        try
        {
            if (errorCode < 0)
            {
                _done.TrySetException(new WebView2Exception("CallDevToolsProtocolMethod", errorCode));
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            if (_enabledAfter < 0)
            {
                _enabledAfter = elapsed;
                _webView.CallDevToolsProtocolMethod("Page.setInterceptFileChooserDialog", """{"enabled":true}""", this);
                return;
            }

            _log.Info($"Page file picker blocked in {elapsed:0.0} ms (Page.enable {_enabledAfter:0.0} ms).");
            _done.TrySetResult();
        }
        catch (Exception exception)
        {
            _done.TrySetException(exception);
        }
    }
}
