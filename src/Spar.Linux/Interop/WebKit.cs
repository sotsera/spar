using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// WebKitGTK 6.0, its JavaScriptCore and libsoup 3, as the host uses them (checked against the 2.52 headers of Ubuntu
/// 24.04; the provisional minimum is 2.48). Everything runs on the main-loop thread. Getters marked "borrowed" return
/// transfer-none pointers the caller never frees.
/// </summary>
internal static unsafe partial class WebKit
{
    /// <summary><c>WEBKIT_USER_CONTENT_INJECT_TOP_FRAME</c>.</summary>
    public const int InjectTopFrame = 1;

    /// <summary><c>WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START</c>.</summary>
    public const int InjectAtDocumentStart = 0;

    /// <summary><c>SOUP_MESSAGE_HEADERS_RESPONSE</c>.</summary>
    public const int SoupHeadersResponse = 1;

    private const string Library = "libwebkitgtk-6.0.so.4";
    private const string Jsc = "libjavascriptcoregtk-6.0.so.1";
    private const string Soup = "libsoup-3.0.so.0";

    [LibraryImport(Library)]
    public static partial uint webkit_get_major_version();

    [LibraryImport(Library)]
    public static partial uint webkit_get_minor_version();

    [LibraryImport(Library)]
    public static partial uint webkit_get_micro_version();

    [LibraryImport(Library)]
    public static partial nuint webkit_web_view_get_type();

    /// <summary>A session whose data and cache live in the two folders; owned.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    [LibraryImport(Library)]
    public static partial nuint webkit_network_session_get_type();

    /// <summary>Owned.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_user_content_manager_new();

    [LibraryImport(Library)]
    public static partial nuint webkit_user_content_manager_get_type();

    /// <summary>
    /// Adds <c>window.webkit.messageHandlers.&lt;name&gt;</c> to the script world <paramref name="worldName"/> only;
    /// its messages arrive as <c>script-message-received::&lt;name&gt;</c> on the manager.
    /// </summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string worldName);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_user_content_manager_unregister_script_message_handler(nint manager, string name, string worldName);

    /// <summary>
    /// A user script run in the script world <paramref name="worldName"/>; owned, <see cref="webkit_user_script_unref"/>.
    /// <paramref name="allowList"/> is a null-terminated array of URL patterns.
    /// </summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new_for_world(
        string source, int injectedFrames, int injectionTime, string worldName, nint* allowList, nint* blockList);

    /// <summary>The manager takes its own reference.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_user_content_manager_add_script(nint manager, nint script);

    [LibraryImport(Library)]
    public static partial void webkit_user_content_manager_remove_all_scripts(nint manager);

    [LibraryImport(Library)]
    public static partial void webkit_user_script_unref(nint script);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_web_view_get_context(nint view);

    /// <summary>
    /// Registers the handler of a URI scheme on the context, for the context's life. <paramref name="data"/> is
    /// borrowed; the callback gets each request borrowed, and refs it to finish later.
    /// </summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_context_register_uri_scheme(
        nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(nint manager, string scheme);

    /// <summary>Borrowed UTF-8.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_uri_scheme_request_get_uri(nint request);

    /// <summary>Borrowed UTF-8.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_uri_scheme_request_get_http_method(nint request);

    /// <summary>A response that reads <paramref name="stream"/>, which it references; owned.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_uri_scheme_response_new(nint stream, long length);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_status(nint response, uint status, string reason);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);

    /// <summary>Takes <paramref name="headers"/> (transfer full).</summary>
    [LibraryImport(Library)]
    public static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);

    /// <summary>The request references the response; the caller still owns its own reference.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint view, string uri);

    [LibraryImport(Library)]
    public static partial void webkit_web_view_reload(nint view);

    /// <summary>Borrowed UTF-8, or 0 before the first load.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_web_view_get_uri(nint view);

    /// <summary>
    /// Runs <paramref name="script"/> (UTF-8, <paramref name="length"/> bytes) in the main frame's script world
    /// <paramref name="worldName"/>, or the page's own world when it is null; no callback, so its result is dropped.
    /// </summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_evaluate_javascript(
        nint view, byte* script, nint length, string? worldName, nint sourceUri, nint cancellable, nint callback, nint data);

    [LibraryImport(Library)]
    public static partial void webkit_web_view_set_background_color(nint view, GdkRgba* color);

    /// <summary>Ends the view's web process; <c>web-process-terminated</c> follows with <c>TERMINATED_BY_API</c>.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_web_view_terminate_web_process(nint view);

    /// <summary>
    /// Whether WebKit thinks the view's web process answers (a <c>gboolean</c>); its change is the
    /// <c>notify::is-web-process-responsive</c> signal.
    /// </summary>
    [LibraryImport(Library)]
    public static partial int webkit_web_view_get_is_web_process_responsive(nint view);

    [LibraryImport(Jsc)]
    public static partial int jsc_value_is_string(nint value);

    /// <summary>Owned UTF-8: <see cref="GLib.TakeString"/>.</summary>
    [LibraryImport(Jsc)]
    public static partial nint jsc_value_to_string(nint value);

    /// <summary>Owned, until <see cref="webkit_uri_scheme_response_set_http_headers"/> takes them.</summary>
    [LibraryImport(Soup)]
    public static partial nint soup_message_headers_new(int type);

    [LibraryImport(Soup, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void soup_message_headers_append(nint headers, string name, string value);
}
