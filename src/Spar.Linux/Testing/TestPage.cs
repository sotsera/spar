using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Spar.Hosting;
using Spar.Linux.Interop;
using static Spar.Linux.Testing.TestNative;

namespace Spar.Linux.Testing;

/// <summary>
/// The page driver's transport on Linux, over WebKitGTK's own API (<c>docs/evidence/window-automation-linux</c>): the engine as
/// a document-start script of an isolated world of its own, each request one <c>call_async_javascript_function</c> in
/// that world, script in the page's world the same way, the page's console messages and errors from a small
/// document-start script of the page's world that hands them, as DOM events, to a relay in the engine's world (WebKitGTK
/// 6.0 has no console signal), script dialogs answered in <c>script-dialog</c>, the documents from <c>load-changed</c> and
/// <c>notify::uri</c>, and screenshots from <c>get_snapshot</c>. Everything here runs on the main loop.
/// </summary>
internal sealed class TestPage
{
    /// <summary>The script message handler of the engine's world, through which the relay reports the page's events.</summary>
    private const string Handler = "sparAutomation";

    /// <summary><c>WEBKIT_LOAD_COMMITTED</c> and <c>WEBKIT_LOAD_FINISHED</c>.</summary>
    private const int LoadCommitted = 2;

    private const int LoadFinished = 3;

    private const string EngineBody = "return globalThis.__sparAutomation.run(request);";

    /// <summary>
    /// The page's side, at document start in the page's world: console calls and uncaught errors become one DOM event
    /// each, with a JSON string as detail, before any page script can change what it uses.
    /// </summary>
    private const string PageSide = """
        (() => {
          const send = globalThis.dispatchEvent.bind(globalThis);
          const Event = globalThis.CustomEvent;
          const json = JSON.stringify;
          const describe = value => {
            if (typeof value === "string") { return value; }
            if (value instanceof Error) { return String(value) + (value.stack ? "\n" + value.stack : ""); }
            try { const text = json(value); return text === undefined ? String(value) : text; } catch { return String(value); }
          };
          const report = (kind, type, text) => send(new Event("spar-automation:page", { detail: json({ kind, type, text }) }));
          for (const type of ["log", "info", "warn", "error", "debug", "trace", "dir", "table", "assert"]) {
            const original = console[type];
            if (typeof original !== "function") { continue; }
            console[type] = function (...values) {
              if (type !== "assert" || !values[0]) {
                report("console", type === "warn" ? "warning" : type, (type === "assert" ? values.slice(1) : values).map(describe).join(" "));
              }
              return original.apply(this, values);
            };
          }
          globalThis.addEventListener("error", event => report("error", "", event.error !== undefined && event.error !== null ? describe(event.error) : String(event.message)));
          globalThis.addEventListener("unhandledrejection", event => report("error", "", "Uncaught (in promise) " + describe(event.reason)));
        })();
        """;

    /// <summary>The relay, in the engine's world: it passes the page side's events to the handler.</summary>
    private const string Relay = """
        (() => {
          const post = window.webkit.messageHandlers.sparAutomation.postMessage.bind(window.webkit.messageHandlers.sparAutomation);
          addEventListener("spar-automation:page", event => { if (typeof event.detail === "string") { post(event.detail); } });
        })();
        """;

    private readonly nint _view;
    private readonly SparTestAutomation _automation;
    private readonly Action<string> _phase;
    private readonly Action<bool> _committed;
    private GCHandle _self;
    private bool _firstLoaded;
    private string _url = "";
    private int _documents;

    private TestPage(nint view, SparTestAutomation automation, Action<string> phase, Action<bool> committed)
    {
        _view = view;
        _automation = automation;
        _phase = phase;
        _committed = committed;
    }

    /// <summary>
    /// How many documents have committed in the view (any thread). An engine request sent to a document that a navigation
    /// then replaced never answers, so a silent request is the page's fault only when this did not change meanwhile.
    /// </summary>
    public int Documents => Volatile.Read(ref _documents);

    /// <summary>Raised once the first app-origin document has loaded.</summary>
    public event Action? FirstLoaded;

    /// <summary>Raised when a load of the main frame failed before its first document loaded.</summary>
    public event Action<string>? FirstLoadFailed;

    /// <summary>
    /// The engine, the page side and the relay as document-start scripts, and the view's signals (loop thread, before
    /// the first load). <paramref name="committed"/> is told of each committed document (true for the first). The devtools
    /// pass <paramref name="answerDialogs"/> false: on the developer's desktop a script dialog is the developer's to answer.
    /// </summary>
    public static unsafe TestPage Install(
        nint view, nint content, SparTestAutomation automation, Action<string> phase, Action<bool> committed, bool answerDialogs = true)
    {
        var page = new TestPage(view, automation, phase, committed);
        page._self = GCHandle.Alloc(page);
        var self = GCHandle.ToIntPtr(page._self);
        if (WebKit.webkit_user_content_manager_register_script_message_handler(content, Handler, automation.World) == 0)
        {
            throw new InvalidOperationException("WebKit refused the test page's script message handler.");
        }

        Connect(content, "script-message-received::" + Handler, (delegate* unmanaged<nint, nint, nint, void>)&OnMessage, self);
        if (automation.PageScript is { } script)
        {
            // A scene's script of the page's own world (its fixed clock), before the page's scripts of every document.
            AddScript(content, webkit_user_script_new(script, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, null, null));
        }

        AddScript(content, WebKit.webkit_user_script_new_for_world(
            automation.EngineScript, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, automation.World, null, null));
        AddScript(content, WebKit.webkit_user_script_new_for_world(
            Relay, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, automation.World, null, null));
        AddScript(content, webkit_user_script_new(PageSide, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, null, null));
        Connect(view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, self);
        Connect(view, "load-failed", (delegate* unmanaged<nint, int, nint, nint, nint, int>)&OnLoadFailed, self);
        Connect(view, "notify::uri", (delegate* unmanaged<nint, nint, nint, void>)&OnUri, self);
        if (answerDialogs)
        {
            Connect(view, "script-dialog", (delegate* unmanaged<nint, nint, nint, int>)&OnScriptDialog, self);
        }

        return page;
    }

    /// <summary>Frees the handle the signals found this page by, once the view is gone.</summary>
    public void Release()
    {
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <summary>One engine request; null when no document's engine answered (a navigation, a lost web process).</summary>
    public async Task<string?> EngineAsync(string request)
    {
        try
        {
            return await CallAsync(EngineBody, _automation.World, request) ?? "null";
        }
        catch (ScriptCallException)
        {
            // The engine answers its own failures as { error }; anything else means no document could run it.
            return null;
        }
    }

    /// <summary>The expression's value as JSON, or null for <c>undefined</c>; a function is called.</summary>
    public async Task<string?> EvaluateAsync(string expression)
    {
        var body = $"const value = (\n{expression}\n);\nreturn typeof value === \"function\" ? value() : value;";
        try
        {
            return await CallAsync(body, worldName: null, argument: null);
        }
        catch (ScriptCallException exception)
        {
            throw new SparTestScriptException(exception.Message);
        }
    }

    /// <summary>Opens <paramref name="address"/> in the view, or reloads it; the host's navigation lock decides as for any.</summary>
    public void Navigate(string? address)
    {
        if (address is null)
        {
            webkit_web_view_reload(_view);
        }
        else
        {
            webkit_web_view_load_uri(_view, address);
        }
    }

    /// <summary>A PNG of the viewport, or of <paramref name="clip"/> in CSS pixels (the test display's scale is 1).</summary>
    public async Task<byte[]> ScreenshotAsync((double X, double Y, double Width, double Height)? clip)
    {
        var texture = await StartSnapshot().Texture.Task;
        try
        {
            return clip is { } area ? Crop(texture, area) : Png(texture);
        }
        finally
        {
            GLib.g_object_unref(texture);
        }
    }

    private unsafe PendingCall StartSnapshot()
    {
        var call = new PendingCall(PendingCall.Kind.Snapshot);
        webkit_web_view_get_snapshot(_view, SnapshotVisible, 0, 0, &OnCallDone, call.Handle);
        return call;
    }

    private static void AddScript(nint content, nint script)
    {
        WebKit.webkit_user_content_manager_add_script(content, script);
        WebKit.webkit_user_script_unref(script);
    }

    private static unsafe void Connect(nint instance, string signal, void* handler, nint self)
    {
        if (GLib.g_signal_connect_data(instance, signal, handler, self, 0, 0) == 0)
        {
            throw new InvalidOperationException($"WebKit has no '{signal}' signal, which the test window needs.");
        }
    }

    private static unsafe byte[] Png(nint texture)
    {
        var bytes = gdk_texture_save_to_png_bytes(texture);
        try
        {
            nuint size;
            var data = g_bytes_get_data(bytes, &size);
            return new ReadOnlySpan<byte>(data, checked((int)size)).ToArray();
        }
        finally
        {
            g_bytes_unref(bytes);
        }
    }

    private static unsafe byte[] Crop(nint texture, (double X, double Y, double Width, double Height) area)
    {
        var width = gdk_texture_get_width(texture);
        var height = gdk_texture_get_height(texture);
        var left = Math.Clamp((int)Math.Floor(area.X), 0, width);
        var top = Math.Clamp((int)Math.Floor(area.Y), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(area.X + area.Width), left, width);
        var bottom = Math.Clamp((int)Math.Ceiling(area.Y + area.Height), top, height);
        var cropWidth = Math.Max(1, right - left);
        var cropHeight = Math.Max(1, bottom - top);
        var pixels = new byte[width * height * 4];
        fixed (byte* all = pixels)
        {
            gdk_texture_download(texture, all, (nuint)(width * 4));
        }

        var cropped = new byte[cropWidth * cropHeight * 4];
        for (var row = 0; row < cropHeight && top + row < height; row++)
        {
            var from = (((top + row) * width) + left) * 4;
            var length = Math.Min(cropWidth, width - left) * 4;
            if (length > 0)
            {
                Buffer.BlockCopy(pixels, from, cropped, row * cropWidth * 4, length);
            }
        }

        fixed (byte* data = cropped)
        {
            var bytes = g_bytes_new(data, (nuint)cropped.Length);
            var piece = gdk_memory_texture_new(cropWidth, cropHeight, MemoryDefault, bytes, (nuint)(cropWidth * 4));
            g_bytes_unref(bytes);
            try
            {
                return Png(piece);
            }
            finally
            {
                GLib.g_object_unref(piece);
            }
        }
    }

    /// <summary>One <c>call_async_javascript_function</c>: its value as JSON, null for <c>undefined</c>.</summary>
    private unsafe Task<string?> CallAsync(string body, string? worldName, string? argument)
    {
        var call = new PendingCall(PendingCall.Kind.Script);
        nint arguments = 0;
        if (argument is not null)
        {
            // The argument as an a{sv} of one string: the variant and the dictionary's end are floating, and WebKit sinks them.
            var dictionary = g_variant_dict_new(0);
            var text = Encoding.UTF8.GetBytes(argument + "\0");
            fixed (byte* value = text)
            {
                g_variant_dict_insert_value(dictionary, "request", g_variant_new_string(value));
            }

            arguments = g_variant_dict_end(dictionary);
            g_variant_dict_unref(dictionary);
        }

        var code = Encoding.UTF8.GetBytes(body);
        fixed (byte* source = code)
        {
            webkit_web_view_call_async_javascript_function(_view, source, code.Length, arguments, worldName, 0, 0, &OnCallDone, call.Handle);
        }

        return call.Value.Task;
    }

    private void LoadChanged(int loadEvent)
    {
        switch (loadEvent)
        {
            case LoadCommitted:
                Interlocked.Increment(ref _documents);
                _url = Marshal.PtrToStringUTF8(webkit_web_view_get_uri(_view)) ?? "";
                _automation.Events.Navigated(_url, newDocument: true);
                _committed(!_firstLoaded);
                break;
            case LoadFinished:
                var address = Marshal.PtrToStringUTF8(webkit_web_view_get_uri(_view)) ?? "";
                if (!_firstLoaded && SchemeServer.IsAppOrigin(address))
                {
                    _firstLoaded = true;
                    _phase("first navigation completed");
                    FirstLoaded?.Invoke();
                }

                break;
        }
    }

    private void UriChanged()
    {
        var address = Marshal.PtrToStringUTF8(webkit_web_view_get_uri(_view)) ?? "";
        if (address.Length > 0 && address != _url)
        {
            // An address change within the document (history.pushState, a fragment); a new document reports its own commit.
            _url = address;
            _automation.Events.Navigated(address, newDocument: false);
        }
    }

    private void Message(nint value)
    {
        if (jsc_value_is_string(value) == 0 || GLib.TakeString(jsc_value_to_string(value)) is not { } text)
        {
            return;
        }

        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var message = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (kind == "console")
        {
            _automation.Events.Console(root.GetProperty("type").GetString() ?? "log", message);
        }
        else if (kind == "error")
        {
            _automation.Events.PageError(message);
        }
    }

    private void Dialog(nint dialog)
    {
        var type = webkit_script_dialog_get_dialog_type(dialog) switch
        {
            1 => "confirm",
            2 => "prompt",
            3 => "beforeunload",
            _ => "alert",
        };
        var message = Marshal.PtrToStringUTF8(webkit_script_dialog_get_message(dialog)) ?? "";
        var accept = _automation.Events.Dialog(type, message);
        switch (type)
        {
            case "confirm" or "beforeunload":
                webkit_script_dialog_confirm_set_confirmed(dialog, accept ? 1 : 0);
                break;
            case "prompt" when accept:
                webkit_script_dialog_prompt_set_text(dialog, webkit_script_dialog_prompt_get_default_text(dialog));
                break;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data) => Dispatch(data, page => page.LoadChanged(loadEvent));

    [UnmanagedCallersOnly]
    private static void OnUri(nint view, nint property, nint data) => Dispatch(data, page => page.UriChanged());

    [UnmanagedCallersOnly]
    private static void OnMessage(nint manager, nint value, nint data) => Dispatch(data, page => page.Message(value));

    /// <summary><c>load-failed</c>: the view, the load event, the address, the <c>GError</c> and the user data; FALSE lets WebKit go on.</summary>
    [UnmanagedCallersOnly]
    private static int OnLoadFailed(nint view, int loadEvent, nint address, nint error, nint data)
    {
        var why = GLib.ErrorMessage(error);
        Dispatch(data, page =>
        {
            if (!page._firstLoaded)
            {
                page._phase("navigation failed");
                page.FirstLoadFailed?.Invoke(why);
            }
        });
        return 0;
    }

    /// <summary><c>script-dialog</c>: answered here (TRUE), so no dialog ever shows in a test window.</summary>
    [UnmanagedCallersOnly]
    private static int OnScriptDialog(nint view, nint dialog, nint data)
    {
        Dispatch(data, page => page.Dialog(dialog));
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnCallDone(nint source, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var call = (PendingCall)handle.Target!;
        handle.Free();
        call.Finish(source, result);
    }

    private static void Dispatch(nint data, Action<TestPage> work)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is TestPage page)
            {
                work(page);
            }
        }
        catch (Exception exception)
        {
            // Nothing may unwind into GLib.
            Console.Error.WriteLine("spar fail: a test page's signal failed: " + exception.Message);
        }
    }

    /// <summary>A script call or a snapshot on its way; the callback completes it on the loop.</summary>
    private sealed class PendingCall
    {
        private readonly Kind _kind;

        public PendingCall(Kind kind)
        {
            _kind = kind;
            Handle = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        }

        public enum Kind
        {
            Script,
            Snapshot,
        }

        public nint Handle { get; }

        public TaskCompletionSource<string?> Value { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<nint> Texture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public unsafe void Finish(nint view, nint result)
        {
            nint error = 0;
            try
            {
                if (_kind == Kind.Snapshot)
                {
                    var texture = webkit_web_view_get_snapshot_finish(view, result, &error);
                    if (texture == 0)
                    {
                        Texture.TrySetException(new InvalidOperationException("The page's snapshot failed: " + GLib.ErrorMessage(error)));
                        return;
                    }

                    Texture.TrySetResult(texture);
                    return;
                }

                var value = webkit_web_view_call_async_javascript_function_finish(view, result, &error);
                if (value == 0)
                {
                    Value.TrySetException(new ScriptCallException(GLib.ErrorMessage(error)));
                    return;
                }

                try
                {
                    Value.TrySetResult(jsc_value_is_undefined(value) != 0 ? null : GLib.TakeString(jsc_value_to_json(value, 0)));
                }
                finally
                {
                    GLib.g_object_unref(value);
                }
            }
            catch (Exception exception)
            {
                Value.TrySetException(exception);
                Texture.TrySetException(exception);
            }
            finally
            {
                if (error != 0)
                {
                    GLib.g_error_free(error);
                }
            }
        }
    }

    /// <summary>A script call WebKit could not complete: the script threw, or no document could run it.</summary>
    private sealed class ScriptCallException(string message) : Exception(message);
}
