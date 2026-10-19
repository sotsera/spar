using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows.Interop;

namespace Sotsera.Spar.Windows;

public sealed unsafe partial class SparWindow
{
    private const uint BridgeMessage = 0x8003;
    private readonly ConcurrentQueue<Action> _bridgePosted = new();
    private readonly List<Action> _documentSubscriptions = [];
    private SparBridgeDocument? _document;
    private string? _localUri;
    private byte[]? _localHtml;
    private ulong _navigationId;
    private int _pendingBridgeWork;
    private bool _expectNavigation;
    private bool _trustedNavigation;
    private bool _reloadRequested;

    /// <summary>Retires admission immediately on the UI thread; reload waits for actual backend completion.</summary>
    public void ReloadDocument()
    {
        Post(() =>
        {
            _document?.Retire();
            _reloadRequested = true;
            if (_pendingBridgeWork == 0)
            {
                LoadLocalDocument();
            }
        });
    }

    private void ConfigureDocumentTransport()
    {
        ConfigureInspection();
        if (Encoding.UTF8.GetByteCount(_options.Html) > 1024 * 1024)
        {
            throw new ArgumentException("Local HTML exceeds the 1 MiB document limit.");
        }

        _localHtml = Encoding.UTF8.GetBytes(_options.Html);
        Check(_webView!.GetSettings(out var pointer), "Get WebView settings");
        var settings = ComReferences.Take<IWebViewSettings>(pointer);
        try
        {
            Check(settings.SetAreHostObjectsAllowed(0), "Disable native host objects");
            Check(settings.SetAreDefaultScriptDialogsEnabled(0), "Disable script dialogs");
            Check(settings.SetIsWebMessageEnabled(_options.Bridge is null ? 0 : 1), "Configure bridge messages");
#if !DEBUG
            Check(settings.SetAreDevToolsEnabled(0), "Disable production browser developer tools");
            Check(settings.SetAreDefaultContextMenusEnabled(0), "Disable production browser context menus");
#endif
        }
        finally
        {
            ComReferences.Release(settings);
        }

        Subscribe(new NavigationStarting(args => Guard(() => NavigationStarting(args, false))), typeof(INavigationStarting).GUID,
            handler =>
            {
                Check(_webView.AddNavigationStarting(handler, out var token), "Subscribe navigation policy");
                return token;
            },
            token => Check(_webView.RemoveNavigationStarting(token), "Unsubscribe navigation policy"));
        Subscribe(new NavigationStarting(args => Guard(() => NavigationStarting(args, true))), typeof(INavigationStarting).GUID,
            handler =>
            {
                Check(_webView.AddFrameNavigationStarting(handler, out var token), "Subscribe frame policy");
                return token;
            },
            token => Check(_webView.RemoveFrameNavigationStarting(token), "Unsubscribe frame policy"));
        Subscribe(new ContentLoading(args => Guard(() => ContentLoading(args))), typeof(IContentLoading).GUID,
            handler =>
            {
                Check(_webView.AddContentLoading(handler, out var token), "Subscribe document identity");
                return token;
            },
            token => Check(_webView.RemoveContentLoading(token), "Unsubscribe document identity"));
        Subscribe(new WebResourceRequested(args => Guard(() => ResourceRequested(args))), typeof(IWebResourceRequested).GUID,
            handler =>
            {
                Check(_webView.AddWebResourceRequested(handler, out var token), "Subscribe local resources");
                return token;
            },
            token => Check(_webView.RemoveWebResourceRequested(token), "Unsubscribe local resources"));
        fixed (char* filter = "*")
        {
            Check(_webView.AddWebResourceRequestedFilter((nint)filter, 0), "Filter all network resources");
        }

        Subscribe(new WebMessageReceived(args => Guard(() => MessageReceived(args))), typeof(IWebMessageReceived).GUID,
            handler =>
            {
                Check(_webView.AddWebMessageReceived(handler, out var token), "Subscribe bridge messages");
                return token;
            },
            token => Check(_webView.RemoveWebMessageReceived(token), "Unsubscribe bridge messages"));
        Subscribe(new NewWindowRequested(args => Guard(() => DenyNewWindow(args))), typeof(INewWindowRequested).GUID,
            handler =>
            {
                Check(_webView.AddNewWindowRequested(handler, out var token), "Subscribe popup policy");
                return token;
            },
            token => Check(_webView.RemoveNewWindowRequested(token), "Unsubscribe popup policy"));
        Subscribe(new PermissionRequested(args => Guard(() => DenyPermission(args))), typeof(IPermissionRequested).GUID,
            handler =>
            {
                Check(_webView.AddPermissionRequested(handler, out var token), "Subscribe browser permissions");
                return token;
            },
            token => Check(_webView.RemovePermissionRequested(token), "Unsubscribe browser permissions"));
    }

    partial void ConfigureInspection();

    private void Subscribe(object handler, Guid id, Func<nint, long> add, Action<long> remove)
    {
        ComReferences.WithCallback(handler, id, pointer =>
        {
            var token = add(pointer);
            _documentSubscriptions.Add(() => remove(token));
        });
    }

    private void LoadLocalDocument()
    {
        if (_closeRequested)
        {
            return;
        }

        _document?.Retire();
        _reloadRequested = false;
        _trustedNavigation = false;
        _localUri = _options.LoopbackContent is { } content
            ? content.BaseUri.AbsoluteUri + "?sparDocument=" + Guid.NewGuid().ToString("N")
            : $"https://{Guid.NewGuid():N}.spar.invalid/index.html";
        _expectNavigation = true;
        fixed (char* uri = _localUri)
        {
            Check(_webView!.Navigate((nint)uri), "Navigate to the owned local document");
        }
    }

    private void NavigationStarting(nint pointer, bool frame)
    {
        var args = ComReferences.Borrow<INavigationStartingArgs>(pointer);
        try
        {
            Check(args.GetUri(out var uriPointer), "Read navigation source");
            var uri = ReadNativeText(uriPointer, 2048);
            if (!frame)
            {
                _document?.Retire();
                _trustedNavigation = false;
            }

            if (frame || _closeRequested || !_expectNavigation || uri != _localUri)
            {
                Check(args.SetCancel(1), "Cancel unauthorized navigation");
                if (!frame && !_closeRequested && _options.LoopbackContent is not null && uri == _localUri)
                {
                    // A framework full-page reload retires first, drains work, then receives a fresh native URI.
                    ReloadDocument();
                    return;
                }

                BridgeEvent(new SparBridgeEvent("rejected", _document?.Id ?? "", 0, null, "navigation_denied"));
                return;
            }

            _expectNavigation = false;
            _trustedNavigation = true;
            Check(args.GetNavigationId(out _navigationId), "Capture native navigation identity");
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private void ContentLoading(nint pointer)
    {
        var args = ComReferences.Borrow<IContentLoadingArgs>(pointer);
        try
        {
            Check(args.GetNavigationId(out var id), "Read loading document identity");
            Check(args.GetIsErrorPage(out var isError), "Read loading error status");
            if (!_closeRequested && _trustedNavigation && id == _navigationId && isError == 0)
            {
                _document = _options.Bridge?.OpenDocument(_localUri!, id, value => RunBridgeUi(() => BridgeEvent(value)),
                    _options.LoopbackContent?.ClientCredential);
            }
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private void ResourceRequested(nint pointer)
    {
        var args = ComReferences.Borrow<IWebResourceArgs>(pointer);
        IWebResourceRequest? request = null;
        nint stream = 0;
        nint response = 0;
        try
        {
            Check(args.GetRequest(out var requestPointer), "Get local resource request");
            request = ComReferences.Take<IWebResourceRequest>(requestPointer);
            Check(request.GetUri(out var uriPointer), "Read resource URI");
            var uri = ReadNativeText(uriPointer, 2048);
            Check(request.GetMethod(out var methodPointer), "Read resource method");
            var method = ReadNativeText(methodPointer, 16);
            if (!_closeRequested && _trustedNavigation && method == "GET"
                && _options.LoopbackContent?.AllowsResource(uri) == true)
            {
                // Only this owner-scoped loopback origin/path may reach the actual SPA server.
                return;
            }

            var allowed = false;
            var content = "Blocked"u8.ToArray();
            var contentType = "text/plain; charset=utf-8";
            if (!_closeRequested && _trustedNavigation && method == "GET")
            {
                if (_options.Assets is { } assets)
                {
                    allowed = assets.TryGet(_localUri!, uri, out var assetBytes, out contentType);
                    if (allowed)
                    {
                        content = assetBytes;
                    }
                }
                else if (uri == _localUri)
                {
                    allowed = true;
                    content = _localHtml!;
                    contentType = "text/html; charset=utf-8";
                }
            }

            var responseHeaders = "Content-Type: " + contentType
                + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
            if (_options.Assets is not null)
            {
                responseHeaders += "\r\nContent-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; "
                    + "img-src 'self'; font-src 'self'; connect-src 'none'; frame-src 'none'; object-src 'none'; "
                    + "base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\nReferrer-Policy: no-referrer";
            }
            fixed (byte* bytes = content)
            {
                // SHCreateMemStream copies the initial contents into native-owned storage.
                stream = NativeMethods.SHCreateMemStream(bytes, (uint)content.Length);
            }

            if (stream == 0)
            {
                throw new InvalidOperationException("Could not allocate the local document stream.");
            }

            fixed (char* reason = allowed ? "OK" : "Forbidden")
            fixed (char* headers = responseHeaders)
            {
                Check(_environment!.CreateWebResourceResponse(stream, allowed ? 200 : 403, (nint)reason,
                    (nint)headers, out response), "Create local resource response");
                Check(args.SetResponse(response), "Supply local document response");
            }
        }
        finally
        {
            if (response != 0)
            {
                Marshal.Release(response);
            }

            if (stream != 0)
            {
                Marshal.Release(stream);
            }

            ComReferences.Release(request);
            ComReferences.Release(args);
        }
    }

    private void MessageReceived(nint pointer)
    {
        var args = ComReferences.Borrow<IWebMessageArgs>(pointer);
        try
        {
            Check(args.GetSource(out var sourcePointer), "Read native web-message source");
            var source = ReadNativeText(sourcePointer, 2048);
            Check(args.GetWebMessageAsJson(out var jsonPointer), "Read web-message envelope");
            var message = ReadNativeText(jsonPointer, SparBridge.MaximumMessageBytes);
            var document = _document;
            if (document is null || source is null || message is null || _closeRequested || !_trustedNavigation)
            {
                BridgeEvent(new SparBridgeEvent("rejected", document?.Id ?? "", 0, null, "document_rejected"));
                return;
            }

            var work = document.Dispatch(source, message, reply => RunBridgeUi(() => SendReply(document, reply)));
            if (work is not null)
            {
                _pendingBridgeWork++;
                _ = work.ContinueWith(completed => RunBridgeUi(() =>
                {
                    _pendingBridgeWork--;
                    if (completed.IsFaulted)
                    {
                        _failure ??= completed.Exception;
                        RequestClose();
                    }

                    if (_reloadRequested && _pendingBridgeWork == 0 && !_closeRequested)
                    {
                        LoadLocalDocument();
                    }

                    ScheduleFinish();
                }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private void SendReply(SparBridgeDocument document, string reply)
    {
        if (_closeRequested || !ReferenceEquals(document, _document) || !document.IsActive
            || !_trustedNavigation || document.NavigationId != _navigationId)
        {
            BridgeEvent(new SparBridgeEvent("reply_suppressed", document.Id, 0, null, "document_retired"));
            return;
        }

        Check(_webView!.GetSource(out var sourcePointer), "Recheck reply document source");
        if (ReadNativeText(sourcePointer, 2048) != document.Source)
        {
            document.Retire();
            BridgeEvent(new SparBridgeEvent("reply_suppressed", document.Id, 0, null, "source_changed"));
            return;
        }

        fixed (char* json = reply)
        {
            Check(_webView.PostWebMessageAsJson((nint)json), "Post a document-bound bridge reply");
        }
    }

    private void RunBridgeUi(Action action)
    {
        if (NativeMethods.GetCurrentThreadId() == _threadId)
        {
            Guard(action);
            return;
        }

        // Only admitted calls post from worker threads; their concurrency and completion queues are bounded.
        _bridgePosted.Enqueue(action);
        if (!NativeMethods.PostMessage(_handle, BridgeMessage, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private void BridgeEvent(SparBridgeEvent value)
    {
        _options.BridgeEvent?.Invoke(value);
    }

    private static void DenyNewWindow(nint pointer)
    {
        var args = ComReferences.Borrow<INewWindowArgs>(pointer);
        try
        {
            Check(args.SetHandled(1), "Deny a new browser window");
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private static void DenyPermission(nint pointer)
    {
        var args = ComReferences.Borrow<IPermissionArgs>(pointer);
        try
        {
            Check(args.SetState(2), "Deny browser permission");
        }
        finally
        {
            ComReferences.Release(args);
        }
    }

    private static string? ReadNativeText(nint pointer, int maximumLength)
    {
        try
        {
            if (pointer == 0)
            {
                return null;
            }

            var characters = (char*)pointer;
            var length = 0;
            while (length <= maximumLength && characters[length] != '\0')
            {
                length++;
            }

            return length > maximumLength ? null : new string(characters, 0, length);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }
}
