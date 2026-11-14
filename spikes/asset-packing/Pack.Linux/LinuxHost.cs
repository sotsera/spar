using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Spar.AssetPacking;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This host runs on Linux only.");
    return 1;
}

return LinuxHost.Run(args);

/// <summary>
/// Minimal WebKitGTK 4.1 host (pieces from the Linux desktop spike): a GTK window, one view, and the <c>spar://app/</c>
/// custom scheme serving the embedded pack. The page's <c>POST /_spar/ready</c> ends the run.
/// <c>--decode eager | background | lazy</c> as on Windows; <c>passthrough</c> serves Brotli entries as stored, with
/// <c>Content-Encoding: br</c>, and decodes nothing.
/// </summary>
internal sealed unsafe class LinuxHost
{
    private const string Origin = "spar://app";

    private static LinuxHost? s_current;

    private readonly string[] _args;
    private readonly string _decode;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _marks = [];
    private Task<FrontendAssets>? _assets;
    private nint _window;
    private nint _context;
    private nint _loop;
    private long _closeRequestedAt;
    private double _assetMs;
    private int _requests;
    private string _probe = "";

    private LinuxHost(string[] args)
    {
        _args = args;
        _decode = Option("--decode") ?? "lazy";
    }

    public static int Run(string[] args)
    {
        var host = new LinuxHost(args);
        var thread = new Thread(host.RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        return 0;
    }

    private string? Option(string name)
    {
        var index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    private void Mark(string name, bool sinceClose = false) =>
        _marks[name] = sinceClose ? Stopwatch.GetElapsedTime(_closeRequestedAt).TotalMilliseconds : _clock.Elapsed.TotalMilliseconds;

    private void RunOnUiThread()
    {
        s_current = this;
        switch (_decode)
        {
            case "eager":
                var assets = FrontendAssets.Open(typeof(LinuxHost).Assembly);
                assets.DecodeAll();
                _assets = Task.FromResult(assets);
                Mark("assets-decoded");
                break;
            case "background":
                _assets = Task.Run(() =>
                {
                    var assets = FrontendAssets.Open(typeof(LinuxHost).Assembly);
                    assets.DecodeAll();
                    Mark("assets-decoded");
                    return assets;
                });
                break;
            default:
                _assets = Task.FromResult(FrontendAssets.Open(typeof(LinuxHost).Assembly));
                break;
        }

        if (Gtk.gtk_init_check(0, 0) == 0)
        {
            Console.Error.WriteLine("gtk_init_check failed: no display?");
            return;
        }

        Mark("gtk-init");
        var dataDirectory = Path.GetFullPath(Option("--data-dir") ?? throw new ArgumentException("Pass --data-dir <folder> (never the user's home)."));
        var manager = GObject.New(
            WebKit.webkit_website_data_manager_get_type(),
            [("base-data-directory", Path.Combine(dataDirectory, "data"), 0, 0), ("base-cache-directory", Path.Combine(dataDirectory, "cache"), 0, 0)]);
        _context = WebKit.webkit_web_context_new_with_website_data_manager(manager);
        GObject.g_object_unref(manager);
        if (_args.Contains("--sandbox"))
        {
            WebKit.webkit_web_context_set_sandbox_enabled(_context, 1);
        }

        WebKit.webkit_web_context_register_uri_scheme(_context, "spar", &OnSchemeRequest, 0, 0);
        var security = WebKit.webkit_web_context_get_security_manager(_context);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, "spar");
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, "spar");
        var webView = GObject.New(WebKit.webkit_web_view_get_type(), [("web-context", null, _context, WebKit.webkit_web_context_get_type())]);
        GObject.Connect(webView, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged);

        _window = Gtk.gtk_window_new(0);
        Gtk.gtk_window_set_default_size(_window, 1024, 768);
        Gtk.gtk_container_add(_window, webView);
        Gtk.gtk_widget_show_all(_window);
        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnWindowDestroyed);
        Mark("window");

        GLib.g_timeout_add_full(0, 20_000, &OnTimeout, 0, 0);
        Mark("navigate");
        WebKit.webkit_web_view_load_uri(webView, Origin + "/" + (Option("--page") ?? ""));
        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);

        GObject.g_object_unref(_context);
        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        Report();
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint webView, int loadEvent, nint data)
    {
        if (loadEvent == WebKit.LoadStarted)
        {
            s_current!.Mark("load-started");
        }
        else if (loadEvent == WebKit.LoadFinished)
        {
            s_current!.Mark("load-finished");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWindowDestroyed(nint widget, nint data) => GLib.g_main_loop_quit(s_current!._loop);

    [UnmanagedCallersOnly]
    private static int OnTimeout(nint data)
    {
        s_current!._probe += " TIMEOUT(no ui-ready in 20 s)";
        s_current.Close();
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnCloseIdle(nint data)
    {
        s_current!.Close();
        return 0;
    }

    private void Close()
    {
        if (_closeRequestedAt == 0)
        {
            _closeRequestedAt = Stopwatch.GetTimestamp();
            Gtk.gtk_window_close(_window);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            s_current!.HandleSchemeRequest(request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SCHEME_EXCEPTION {exception}");
            Respond(request, 500, "text/plain", "host error"u8, copy: true, encoding: null);
        }
    }

    private void HandleSchemeRequest(nint request)
    {
        var started = Stopwatch.GetTimestamp();
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        if (path == "/_spar/ready")
        {
            Mark("ui-ready");
            Respond(request, 204, "text/plain", [], copy: false, encoding: null);
            GLib.g_idle_add_full(0, &OnCloseIdle, 0, 0);
            return;
        }

        if (path.StartsWith("/_spar/probe/", StringComparison.Ordinal))
        {
            _probe += " " + Uri.UnescapeDataString(path["/_spar/probe/".Length..]);
            Respond(request, 204, "text/plain", [], copy: false, encoding: null);
            return;
        }

        if (path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            // No backend in this spike: the page's other calls fail and it still renders.
            Respond(request, 404, "application/json", "{\"error\":\"no-backend\"}"u8, copy: true, encoding: null);
            return;
        }

#if SPAR_ENCODING_PROBE
        if (EncodingProbe.Answer(path) is { } probe)
        {
            Respond(request, 200, probe.ContentType, probe.Content, copy: true, probe.Encoding);
            return;
        }
#endif

        var assets = _assets!.GetAwaiter().GetResult();
        _requests++;
        var key = path == "/" ? "index.html" : path[1..];
        if (_decode == "passthrough" && assets.Entries.FirstOrDefault(entry => entry.Path == key) is { Encoding: 1 } encoded)
        {
            Respond(request, 200, encoded.ContentType, assets.Stored(encoded).Span, copy: false, encoding: "br");
        }
        else if (assets.TryGet(key, out var entry, out var content))
        {
            // Stored (identity) entries live in the image: no copy. Decoded ones are managed arrays: copied.
            Respond(request, 200, entry.ContentType, content.Span, copy: entry.Encoding != 0, encoding: null);
        }
        else
        {
            Respond(request, 404, "text/plain", "not found"u8, copy: true, encoding: null);
        }

        _assetMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static void Respond(nint request, int status, string contentType, ReadOnlySpan<byte> content, bool copy, string? encoding)
    {
        nint bytes;
        fixed (byte* data = content)
        {
            bytes = copy ? GLib.g_bytes_new(data, (nuint)content.Length) : GLib.g_bytes_new_static(data, (nuint)content.Length);
        }

        var stream = Gio.g_memory_input_stream_new_from_bytes(bytes);
        GLib.g_bytes_unref(bytes);
        var response = WebKit.webkit_uri_scheme_response_new(stream, content.Length);
        WebKit.webkit_uri_scheme_response_set_status(response, (uint)status, null);
        WebKit.webkit_uri_scheme_response_set_content_type(response, contentType);
        var headers = Soup.soup_message_headers_new(Soup.MessageHeadersResponse);
        Soup.soup_message_headers_append(headers, "Cache-Control", "no-store");
        if (encoding is not null)
        {
            Soup.soup_message_headers_append(headers, "Content-Encoding", encoding);
        }

        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GObject.g_object_unref(response);
        GObject.g_object_unref(stream);
    }

    private void Report()
    {
        var assets = _assets!.IsCompletedSuccessfully ? _assets.Result : null;
        double Get(string name) => _marks.GetValueOrDefault(name, double.NaN);
        Console.WriteLine(
            $"HOST format={FrontendAssets.Format} decode={_decode} assets-decoded={Get("assets-decoded"):F1} gtk-init={Get("gtk-init"):F1} " +
            $"window={Get("window"):F1} navigate={Get("navigate"):F1} load-started={Get("load-started"):F1} load-finished={Get("load-finished"):F1} " +
            $"ui-ready={Get("ui-ready"):F1} navigate->ready={Get("ui-ready") - Get("navigate"):F1} load-started->ready={Get("ui-ready") - Get("load-started"):F1} " +
            $"requests={_requests} asset-handler-ms={_assetMs:F2} cached={assets?.CachedBytes ?? 0} " +
            $"app-rss-kb={Get("rss")} app-pss-kb={Get("pss")} app-hwm-kb={Get("hwm")} close->children-exited={Get("children-exited"):F0}{_probe}");
    }

    private void WaitForChildren()
    {
        // Memory of the app process (GTK + WebKit's UI side + .NET) before the children go.
        _marks["rss"] = Status("status", "VmRSS:");
        _marks["hwm"] = Status("status", "VmHWM:");
        _marks["pss"] = Status("smaps_rollup", "Pss:");
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            if (!HasChildren())
            {
                Mark("children-exited", sinceClose: true);
                return;
            }

            // WebKit tears its processes down through IPC handled on the main context: keep pumping it.
            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(1);
        }
    }

    private static bool HasChildren()
    {
        var self = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            try
            {
                // /proc/<pid>/stat: "pid (comm) state ppid …"; comm may contain spaces, so split after the last ')'.
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (rest[0] != "Z" && rest[1] == self)
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private static double Status(string file, string key)
    {
        foreach (var line in File.ReadLines("/proc/self/" + file))
        {
            if (line.StartsWith(key, StringComparison.Ordinal))
            {
                return double.Parse(line[key.Length..].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            }
        }

        return double.NaN;
    }
}
