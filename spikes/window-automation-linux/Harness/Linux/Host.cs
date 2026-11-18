using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Spar.WindowAutomation.Linux.Native;

namespace Spar.WindowAutomation.Linux;

/// <summary>
/// The process-wide part of the host: the default web context with the <c>spar</c> scheme registered as secure and
/// CORS-enabled, serving the built page folder (client-side routes get <c>index.html</c>).
/// </summary>
internal static unsafe class Session
{
    public const string Origin = "spar://app";

    public static string PageFolder { get; set; } = "";

    /// <summary>The Content-Security-Policy header, or null for none.</summary>
    public static string? Csp { get; set; }

    /// <summary>enable-developer-extras on every view (part 1 probes only).</summary>
    public static bool DeveloperExtras { get; set; }

    public static int Served;

    public static nint Context { get; private set; }

    /// <summary>Must run on the GTK thread, before the first view.</summary>
    public static void Initialize(bool automationAllowed = false)
    {
        Context = WebKit.webkit_web_context_get_default();
        WebKit.webkit_web_context_register_uri_scheme(Context, "spar", &OnSchemeRequest, 0, 0);
        var security = WebKit.webkit_web_context_get_security_manager(Context);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, "spar");
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, "spar");
        if (automationAllowed)
        {
            WebKit.webkit_web_context_set_automation_allowed(Context, 1);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        Interlocked.Increment(ref Served);
        var path = (GLib.Utf8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/").TrimStart('/');
        if (path.Length == 0 || !Path.HasExtension(path))
        {
            path = "index.html";
        }

        var file = Path.GetFullPath(Path.Combine(PageFolder, path));
        var found = file.StartsWith(PageFolder, StringComparison.Ordinal) && File.Exists(file);
        var content = found ? File.ReadAllBytes(file) : "not found"u8.ToArray();
        var type = found ? ContentType(file) : "text/plain";
        nint bytes;
        fixed (byte* pointer = content)
        {
            bytes = GLib.g_bytes_new(pointer, (nuint)content.Length);
        }

        var stream = GLib.g_memory_input_stream_new_from_bytes(bytes);
        GLib.g_bytes_unref(bytes);
        var response = WebKit.webkit_uri_scheme_response_new(stream, content.Length);
        GLib.g_object_unref(stream);
        WebKit.webkit_uri_scheme_response_set_status(response, found ? 200u : 404u, found ? "OK" : "Not Found");
        WebKit.webkit_uri_scheme_response_set_content_type(response, type);
        var headers = Soup.soup_message_headers_new(1);
        Soup.soup_message_headers_append(headers, "Content-Type", type);
        Soup.soup_message_headers_append(headers, "Cache-Control", "no-store");
        if (Csp is not null && type.StartsWith("text/html", StringComparison.Ordinal))
        {
            Soup.soup_message_headers_append(headers, "Content-Security-Policy", Csp);
        }

        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GLib.g_object_unref(response);
    }

    private static string ContentType(string file) => Path.GetExtension(file) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}

/// <summary>
/// One test's window: a GTK window with a WebKitWebView on its own network session (ephemeral by default), the
/// automation engine injected as a document-start user script in an isolated world, and a main-world shim that reports
/// console calls and page errors. Script dialogs are answered at once; the file chooser never opens (files the test
/// armed are selected instead). All GTK and WebKit calls run on the UI thread.
/// </summary>
internal sealed class View
{
    public const string World = "spar-automation";

    private const string MainWorldShim = """
        (() => {
          const post = (kind, text) => { try { webkit.messageHandlers.sparAutomation.postMessage(kind + '\u0001' + text); } catch {} };
          for (const level of ['log', 'info', 'warn', 'error', 'debug']) {
            const original = console[level];
            console[level] = function (...args) { post('console', level + ': ' + args.map(a => { try { return typeof a === 'string' ? a : JSON.stringify(a); } catch { return String(a); } }).join(' ')); return original.apply(this, args); };
          }
          addEventListener('error', e => post('pageerror', e.message + (e.error && e.error.stack ? ' at ' + e.error.stack.split(String.fromCharCode(10))[0] : '')));
          addEventListener('unhandledrejection', e => post('pageerror', 'unhandled rejection: ' + String(e.reason)));
        })();
        """;

    private static readonly Lazy<string> s_engine = new(() =>
    {
        using var stream = typeof(View).Assembly.GetManifestResourceStream("engine.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private readonly ConcurrentQueue<string> _console = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly ConcurrentQueue<string> _dialogs = new();
    private nint _self;
    private TaskCompletionSource? _loaded;
    private string[]? _armedFiles;
    private int _fileChoosers;
    private int _newWindows;

    private View()
    {
    }

    public nint Window { get; private set; }

    public nint WebView { get; private set; }

    public nint NetworkSession { get; private set; }

    public IReadOnlyCollection<string> ConsoleMessages => _console;

    public IReadOnlyCollection<string> PageErrors => _errors;

    public IReadOnlyCollection<string> Dialogs => _dialogs;

    /// <summary><c>run-file-chooser</c> requests the host answered (the native picker never opened).</summary>
    public int FileChoosers => Volatile.Read(ref _fileChoosers);

    /// <summary><c>create</c> requests (window.open that passed WebKit's popup blocker); answered with no window.</summary>
    public int NewWindowRequests => Volatile.Read(ref _newWindows);

    public string? TerminatedReason { get; private set; }

    /// <summary>Creates the window and view on the UI thread. <paramref name="session"/> 0 means a new ephemeral one.</summary>
    public static Task<View> CreateAsync(nint session = 0, int width = 1008, int height = 729) => Ui.Invoke(() => Create(session, width, height));

    public static unsafe View Create(nint session, int width, int height, bool automation = false)
    {
        var view = new View();
        view._self = Handle.Of(view);
        view.NetworkSession = session != 0 ? GLib.g_object_ref(session) : WebKit.webkit_network_session_new_ephemeral();
        var manager = WebKit.webkit_user_content_manager_new();
        var engine = WebKit.webkit_user_script_new_for_world(s_engine.Value, 1, 0, World, 0, 0);
        WebKit.webkit_user_content_manager_add_script(manager, engine);
        WebKit.webkit_user_script_unref(engine);
        var shim = WebKit.webkit_user_script_new(MainWorldShim, 1, 0, 0, 0);
        WebKit.webkit_user_content_manager_add_script(manager, shim);
        WebKit.webkit_user_script_unref(shim);
        WebKit.webkit_user_content_manager_register_script_message_handler(manager, "sparAutomation", null);
        GLib.g_signal_connect_data(manager, "script-message-received::sparAutomation", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage, view._self, 0, 0);

        view.WebView = automation
            ? GLib.New(WebKit.webkit_web_view_get_type(), ("network-session", view.NetworkSession, null), ("user-content-manager", manager, null), ("is-controlled-by-automation", 0, true))
            : GLib.New(WebKit.webkit_web_view_get_type(), ("network-session", view.NetworkSession, null), ("user-content-manager", manager, null));
        GLib.g_object_unref(manager);
        var settings = WebKit.webkit_web_view_get_settings(view.WebView);
        WebKit.webkit_settings_set_hardware_acceleration_policy(settings, WebKit.HardwareAccelerationNever);
        if (Session.DeveloperExtras)
        {
            WebKit.webkit_settings_set_enable_developer_extras(settings, 1);
        }

        GLib.g_signal_connect_data(view.WebView, "load-changed", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, view._self, 0, 0);
        GLib.g_signal_connect_data(view.WebView, "script-dialog", (nint)(delegate* unmanaged<nint, nint, nint, int>)&OnScriptDialog, view._self, 0, 0);
        GLib.g_signal_connect_data(view.WebView, "run-file-chooser", (nint)(delegate* unmanaged<nint, nint, nint, int>)&OnFileChooser, view._self, 0, 0);
        GLib.g_signal_connect_data(view.WebView, "create", (nint)(delegate* unmanaged<nint, nint, nint, nint>)&OnCreate, view._self, 0, 0);
        GLib.g_signal_connect_data(view.WebView, "web-process-terminated", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnTerminated, view._self, 0, 0);

        view.Window = Gtk.gtk_window_new();
        Gtk.gtk_window_set_title(view.Window, "Orders");
        Gtk.gtk_window_set_default_size(view.Window, width, height);
        Gtk.gtk_window_set_child(view.Window, view.WebView);
        return view;
    }

    /// <summary>Load first, then present (the host's order: the page gets focus from the start).</summary>
    public Task NavigateAsync(string url, bool present = true) => Ui.InvokeAsync(async () =>
    {
        _loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebKit.webkit_web_view_load_uri(WebView, url);
        if (present)
        {
            Gtk.gtk_window_present(Window);
            Gtk.gtk_widget_grab_focus(WebView);
        }

        await _loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return true;
    });

    /// <summary>
    /// Runs <paramref name="body"/> (a function body; may <c>await</c>) in <paramref name="world"/> and returns its
    /// result as JSON (null for undefined). Throws <see cref="PageException"/> for a script exception.
    /// </summary>
    public unsafe Task<string?> CallAsync(string body, string? world = World)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui.Post(() =>
        {
            var data = Handle.Of(new Call(this, done));
            WebKit.webkit_web_view_call_async_javascript_function(WebView, body, -1, 0, world, null, 0, &OnCallFinished, data);
        });
        return done.Task;
    }

    /// <summary>The visible region as a GdkTexture's pixels (premultiplied BGRA), with its size.</summary>
    public unsafe Task<(byte[] Pixels, int Width, int Height)> SnapshotAsync()
    {
        var done = new TaskCompletionSource<(byte[], int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui.Post(() =>
        {
            var data = Handle.Of(new Snapshot(this, done));
            WebKit.webkit_web_view_get_snapshot(WebView, 0, 0, 0, &OnSnapshotFinished, data);
        });
        return done.Task;
    }

    /// <summary>The view's origin in X root-window coordinates: the surface's XID, the surface transform and the view's place in the window.</summary>
    public unsafe Task<(nuint Xid, double X, double Y, int Width, int Height)> GeometryAsync() => Ui.Invoke(() =>
    {
        var surface = Gtk.gtk_native_get_surface(Window);
        var xid = surface == 0 ? 0 : Gtk.gdk_x11_surface_get_xid(surface);
        Gtk.gtk_native_get_surface_transform(Window, out var sx, out var sy);
        GraphenePoint origin = default, result = default;
        Gtk.gtk_widget_compute_point(WebView, Window, &origin, &result);
        return (xid, sx + result.X, sy + result.Y, Gtk.gtk_widget_get_width(WebView), Gtk.gtk_widget_get_height(WebView));
    });

    /// <summary>The next <c>run-file-chooser</c> selects these files instead of opening a picker.</summary>
    public void ArmFiles(string[] paths) => _armedFiles = paths;

    public Task CloseAsync() => Ui.Invoke(() =>
    {
        if (Window != 0)
        {
            Gtk.gtk_window_destroy(Window);
            Window = 0;
            WebView = 0;
        }

        if (NetworkSession != 0)
        {
            GLib.g_object_unref(NetworkSession);
            NetworkSession = 0;
        }

        // The view's GCHandle is kept (one per test): a late script message or signal must never meet a freed handle.
        _loaded?.TrySetCanceled();
    });

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint webView, int loadEvent, nint data)
    {
        if (loadEvent == WebKit.LoadFinished)
        {
            Handle.Get<View>(data)._loaded?.TrySetResult();
        }
    }

    [UnmanagedCallersOnly]
    private static int OnScriptDialog(nint webView, nint dialog, nint data)
    {
        var view = Handle.Get<View>(data);
        view._dialogs.Enqueue(WebKit.webkit_script_dialog_get_dialog_type(dialog) + ": " + GLib.Utf8(WebKit.webkit_script_dialog_get_message(dialog)));
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnFileChooser(nint webView, nint request, nint data)
    {
        var view = Handle.Get<View>(data);
        Interlocked.Increment(ref view._fileChoosers);
        var files = Interlocked.Exchange(ref view._armedFiles, null);
        if (files is null)
        {
            WebKit.webkit_file_chooser_request_cancel(request);
            return 1;
        }

        var pointers = new nint[files.Length + 1];
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                pointers[i] = Marshal.StringToCoTaskMemUTF8(files[i]);
            }

            WebKit.webkit_file_chooser_request_select_files(request, pointers);
        }
        finally
        {
            foreach (var pointer in pointers)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        return 1;
    }

    [UnmanagedCallersOnly]
    private static nint OnCreate(nint webView, nint navigationAction, nint data)
    {
        Interlocked.Increment(ref Handle.Get<View>(data)._newWindows);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void OnTerminated(nint webView, int reason, nint data) => Handle.Get<View>(data).TerminatedReason = reason.ToString();

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint value, nint data)
    {
        if (data == 0)
        {
            return;
        }

        var view = Handle.Get<View>(data);
        var text = GLib.Utf8(WebKit.jsc_value_to_string(value)) ?? "";
        var split = text.IndexOf('\u0001');
        if (split < 0)
        {
            return;
        }

        (text[..split] == "console" ? view._console : view._errors).Enqueue(text[(split + 1)..]);
    }

    [UnmanagedCallersOnly]
    private static void OnCallFinished(nint source, nint result, nint data)
    {
        var call = Handle.Get<Call>(data);
        Handle.Free(data);
        var value = WebKit.webkit_web_view_call_async_javascript_function_finish(source, result, out var error);
        if (value == 0)
        {
            call.Done.TrySetException(new PageException(GLib.TakeError(error)));
            return;
        }

        var json = WebKit.jsc_value_to_json(value, 0);
        call.Done.TrySetResult(GLib.Utf8(json));
        GLib.g_free(json);
        GLib.g_object_unref(value);
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnSnapshotFinished(nint source, nint result, nint data)
    {
        var snapshot = Handle.Get<Snapshot>(data);
        Handle.Free(data);
        var texture = WebKit.webkit_web_view_get_snapshot_finish(source, result, out var error);
        if (texture == 0)
        {
            snapshot.Done.TrySetException(new InvalidOperationException("snapshot failed: " + GLib.TakeError(error)));
            return;
        }

        var width = Gtk.gdk_texture_get_width(texture);
        var height = Gtk.gdk_texture_get_height(texture);
        var pixels = new byte[width * height * 4];
        fixed (byte* pointer = pixels)
        {
            Gtk.gdk_texture_download(texture, pointer, (nuint)(width * 4));
        }

        GLib.g_object_unref(texture);
        snapshot.Done.TrySetResult((pixels, width, height));
    }

    private sealed record Call(View View, TaskCompletionSource<string?> Done);

    private sealed record Snapshot(View View, TaskCompletionSource<(byte[], int, int)> Done);
}

/// <summary>An exception thrown by page script that the harness ran.</summary>
internal sealed class PageException(string message) : Exception(message);

/// <summary>A wait that ran out of time, with what was found at the last attempt.</summary>
internal sealed class WaitTimeoutException(string message) : TimeoutException(message);

/// <summary>Process counts by name (pgrep -x semantics, through /proc).</summary>
internal static class Processes
{
    public static int Count(string name)
    {
        var count = 0;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            try
            {
                if (int.TryParse(Path.GetFileName(directory), out var id) && File.ReadAllText(Path.Combine(directory, "comm")).Trim() == name && IsDescendant(id))
                {
                    count++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return count;
    }

    /// <summary>Whether the process descends from this one (WebKit's children, directly or through bubblewrap).</summary>
    public static bool IsDescendant(int pid)
    {
        for (var hops = 0; pid > 1 && hops < 16; hops++)
        {
            if (pid == Environment.ProcessId)
            {
                return true;
            }

            pid = (int)StatusKilobytes(pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "PPid");
        }

        return false;
    }

    /// <summary>A field of /proc/&lt;pid&gt;/status in kB (VmRSS, RssAnon, …), 0 when absent.</summary>
    public static long StatusKilobytes(string pid, string field)
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/status"))
            {
                if (line.StartsWith(field + ":", StringComparison.Ordinal))
                {
                    return long.Parse(line[(field.Length + 1)..].Trim().Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch (IOException)
        {
        }

        return 0;
    }

    /// <summary>The summed VmRSS (kB) of the processes with this name (this run's display has only this harness).</summary>
    public static long RssKilobytes(string name)
    {
        long total = 0;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            var pid = Path.GetFileName(directory);
            try
            {
                if (int.TryParse(pid, out var id) && File.ReadAllText(Path.Combine(directory, "comm")).Trim() == name && IsDescendant(id))
                {
                    total += StatusKilobytes(pid, "VmRSS");
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }
}
