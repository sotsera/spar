using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Linux.Desktop;

namespace Spar.Linux;

/// <summary>
/// One GTK4 window with a WebKitGTK 6.0 view: its own web context and network session (data under the app's
/// folders), the packaged page on spar://app, and the bridge as script messages answered with a reply.
/// Everything here runs on the GTK thread; commands run on the thread pool.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WebKitWindow : ISparTestWindow
{
    private const string Origin = "spar://app";

    private readonly SparRuntime _runtime;
    private readonly GtkThread _gtk;
    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GCHandle _self;
    private nint _window;
    private nint _view;
    private nint _context;

    private WebKitWindow(SparRuntime runtime, GtkThread gtk)
    {
        _runtime = runtime;
        _gtk = gtk;
    }

    /// <summary>WebKitGTK does not report its process ids; tests find them as children of the test process.</summary>
    public IReadOnlyList<int> EngineProcessIds => [];

    public Task Destroyed => _destroyed.Task;

    public static Task<ISparTestWindow> OpenForTestAsync(SparRuntime runtime, CancellationToken cancellationToken)
    {
        var gtk = GtkThread.Shared;
        return gtk.InvokeAsync<ISparTestWindow>(async () =>
        {
            var window = new WebKitWindow(runtime, gtk);
            await window.CreateAsync();
            return window;
        }).WaitAsync(cancellationToken);
    }

    public static Task<WebKitWindow> OpenAsync(SparRuntime runtime, GtkThread gtk) => gtk.InvokeAsync(async () =>
    {
        var window = new WebKitWindow(runtime, gtk);
        await window.CreateAsync();
        return window;
    });

    public Task<string> EvaluateAsync(string asyncFunctionBody, CancellationToken cancellationToken)
    {
        var body = $"return JSON.stringify(await (async () => {{ {asyncFunctionBody} }})());";
        return _gtk.InvokeAsync(() => StartEvaluate(body)).WaitAsync(cancellationToken);
    }

    private unsafe Task<string> StartEvaluate(string body)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        var utf8 = Encoding.UTF8.GetBytes(body);
        fixed (byte* script = utf8)
        {
            WebKit.webkit_web_view_call_async_javascript_function(_view, script, utf8.Length, 0, 0, 0, 0, &OnEvaluated, GCHandle.ToIntPtr(handle));
        }

        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _gtk.InvokeAsync(Teardown);

        // Late callbacks may still name this window; free the handle on a later turn of the loop.
        await Task.Delay(200);
        await _gtk.InvokeAsync(() => _self.Free());
        _closing.Dispose();
    }

    internal void Teardown()
    {
        _closing.Cancel();
        if (_window != 0)
        {
            Gtk.gtk_window_destroy(_window);
            _window = 0;
        }

        if (_context != 0)
        {
            GObject.g_object_unref(_context);
            _context = 0;
        }
    }

    private async Task CreateAsync()
    {
        CreateWindowAndView();
        await _loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private unsafe void CreateWindowAndView()
    {
        _self = GCHandle.Alloc(this);
        var self = GCHandle.ToIntPtr(_self);
        var folders = _runtime.Folders;
        var session = WebKit.webkit_network_session_new(Path.Combine(folders.WebView, "data"), Path.Combine(folders.WebView, "cache"));
        _context = WebKit.webkit_web_context_new();
        WebKit.webkit_web_context_register_uri_scheme(_context, "spar", &OnSchemeRequest, self, 0);
        var security = WebKit.webkit_web_context_get_security_manager(_context);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, "spar");
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, "spar");

        _view = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new("web-context", _context, WebKit.webkit_web_context_get_type()),
            new("network-session", session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_unref(session);

        var manager = WebKit.webkit_web_view_get_user_content_manager(_view);
        WebKit.webkit_user_content_manager_register_script_message_handler_with_reply(manager, "spar", null);
        GObject.g_signal_connect_data(manager, "script-message-with-reply-received::spar", (delegate* unmanaged<nint, nint, nint, nint, int>)&OnMessage, self, 0, 0);
        GObject.g_signal_connect_data(_view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, self, 0, 0);

        _window = Gtk.gtk_window_new();
        Gtk.gtk_window_set_title(_window, _runtime.Window.Title);
        Gtk.gtk_window_set_default_size(_window, _runtime.Window.Width, _runtime.Window.Height);
        Gtk.gtk_window_set_child(_window, _view);
        GObject.g_signal_connect_data(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnDestroy, self, 0, 0);

        // Load first, then show (spikes/linux-wsl-desktop): no stall, and the page has focus from the start.
        WebKit.webkit_web_view_load_uri(_view, Origin + "/index.html");
        Gtk.gtk_window_present(_window);
    }

    private async Task AnswerAsync(string message, nint reply, nint context)
    {
        var token = _closing.Token;
        try
        {
            var answer = await Task.Run(() => _runtime.Bridge.HandleAsync(message, token), CancellationToken.None);
            var value = Jsc.jsc_value_new_string(context, answer);
            WebKit.webkit_script_message_reply_return_value(reply, value);
            GObject.g_object_unref(value);
        }
        finally
        {
            WebKit.webkit_script_message_reply_unref(reply);
            GObject.g_object_unref(context);
        }
    }

    private static WebKitWindow? From(nint data) => data == 0 ? null : GCHandle.FromIntPtr(data).Target as WebKitWindow;

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        var window = From(data);
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        byte[]? content = null;
        var found = window is not null && window._runtime.Assets.TryGet(path, out content);
        var body = found ? content! : Encoding.UTF8.GetBytes("Not found");

        var stream = Gio.MemoryStream(body);
        var response = WebKit.webkit_uri_scheme_response_new(stream, body.Length);
        GObject.g_object_unref(stream);
        WebKit.webkit_uri_scheme_response_set_status(response, found ? 200u : 404u, null);
        var contentType = found ? SparAssets.ContentType(path == "/" ? "/index.html" : path) : "text/plain";
        WebKit.webkit_uri_scheme_response_set_content_type(response, contentType);

        // set_http_headers replaces the content type set above, so it goes in the header set too: without it,
        // nosniff blocks the script (observed: fetch saw status 200 and no Content-Type).
        var headers = Soup.soup_message_headers_new(1);
        Soup.soup_message_headers_append(headers, "Content-Type", contentType);
        Soup.soup_message_headers_append(headers, "Cache-Control", "no-store");
        Soup.soup_message_headers_append(headers, "X-Content-Type-Options", "nosniff");
        if (found)
        {
            Soup.soup_message_headers_append(headers, "Content-Security-Policy", SparAssets.ContentSecurityPolicy);
        }

        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GObject.g_object_unref(response);
    }

    [UnmanagedCallersOnly]
    private static int OnMessage(nint manager, nint value, nint reply, nint data)
    {
        var window = From(data);
        if (window is null || window._closing.IsCancellationRequested)
        {
            return 0;
        }

        var message = GLib.TakeString(Jsc.jsc_value_to_string(value)) ?? "";
        var context = GObject.g_object_ref(Jsc.jsc_value_get_context(value));
        WebKit.webkit_script_message_reply_ref(reply);
        _ = window.AnswerAsync(message, reply, context);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data)
    {
        if (loadEvent == WebKit.LoadFinished)
        {
            From(data)?._loaded.TrySetResult();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDestroy(nint widget, nint data)
    {
        if (From(data) is { } window)
        {
            window._window = 0;
            window._destroyed.TrySetResult();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvaluated(nint source, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var completion = (TaskCompletionSource<string>)handle.Target!;
        handle.Free();
        var value = WebKit.webkit_web_view_call_async_javascript_function_finish(source, result, out var error);
        if (value == 0)
        {
            var message = GLib.ErrorMessage(error);
            GLib.g_error_free(error);
            completion.TrySetException(new InvalidOperationException("The page threw: " + message));
            return;
        }

        var json = GLib.TakeString(Jsc.jsc_value_to_string(value)) ?? "null";
        GObject.g_object_unref(value);
        completion.TrySetResult(json);
    }
}

/// <summary>Production: one GTK thread, one window, until the user closes it.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxHost
{
    public static int RunUntilClosed(SparRuntime runtime)
    {
        var gtk = GtkThread.Start("Spar UI");
        var window = WebKitWindow.OpenAsync(runtime, gtk).GetAwaiter().GetResult();
        window.Destroyed.GetAwaiter().GetResult();
        gtk.InvokeAsync(window.Teardown).GetAwaiter().GetResult();
        gtk.Quit();
        gtk.Thread.Join();
        return 0;
    }
}
