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

    /// <summary><c>WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION</c>: a document of the main frame or of a frame.</summary>
    public const int PolicyNavigation = 0;

    /// <summary><c>WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION</c>.</summary>
    public const int PolicyNewWindow = 1;

    /// <summary><c>WEBKIT_POLICY_DECISION_TYPE_RESPONSE</c>.</summary>
    public const int PolicyResponse = 2;

    /// <summary><c>WEBKIT_PERMISSION_STATE_DENIED</c>.</summary>
    public const int PermissionDenied = 1;

    /// <summary><c>WEBKIT_PERMISSION_STATE_PROMPT</c>.</summary>
    public const int PermissionPrompt = 2;

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

    /// <summary>A script of the page's own world (the telemetry layer's settings, read by the page's SDK).</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint* allowList, nint* blockList);

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

    /// <summary>The request's <c>SoupMessageHeaders</c>, borrowed (transfer none); since 2.36.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_uri_scheme_request_get_http_headers(nint request);

    /// <summary>One header's value, borrowed UTF-8, or 0 when the request has none.</summary>
    [LibraryImport(Soup, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint soup_message_headers_get_one(nint headers, string name);

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

    /// <summary>Stops the view's current load; <c>load-failed</c> follows with a cancellation, and the document stays.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_web_view_stop_loading(nint view);

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

    /// <summary>The session's data manager; borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_network_session_get_website_data_manager(nint session);

    /// <summary>The folder the manager keeps its data in; borrowed UTF-8, or 0 for an ephemeral one.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_website_data_manager_get_base_data_directory(nint manager);

    /// <summary>The folder the manager keeps its caches in; borrowed UTF-8, or 0 for an ephemeral one.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_website_data_manager_get_base_cache_directory(nint manager);

    /// <summary>A request's body as a stream the caller owns (<see cref="GLib.g_object_unref"/>), or 0 when it has none.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_uri_scheme_request_get_http_body(nint request);

    /// <summary>The navigation of a navigation or new-window decision; borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_navigation_policy_decision_get_navigation_action(nint decision);

    /// <summary>The navigation's request; borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_navigation_action_get_request(nint action);

    /// <summary>Whether a person's input started the navigation (a <c>gboolean</c>).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_navigation_action_is_user_gesture(nint action);

    /// <summary>Whether the navigation follows a redirect (a <c>gboolean</c>).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_navigation_action_is_redirect(nint action);

    /// <summary><c>WebKitNavigationType</c>: 0 link, 1 form, 2 back or forward, 3 reload, 4 form again, 5 other.</summary>
    [LibraryImport(Library)]
    public static partial int webkit_navigation_action_get_navigation_type(nint action);

    /// <summary>Borrowed UTF-8.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_uri_request_get_uri(nint request);

    /// <summary>Answers a policy decision: the navigation, window or response does not happen.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_policy_decision_ignore(nint decision);

    /// <summary>A response decision's request; borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_response_policy_decision_get_request(nint decision);

    /// <summary>Whether the response is the main frame's document (a <c>gboolean</c>).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_response_policy_decision_is_main_frame_main_resource(nint decision);

    [LibraryImport(Library)]
    public static partial void webkit_permission_request_allow(nint request);

    [LibraryImport(Library)]
    public static partial void webkit_permission_request_deny(nint request);

    /// <summary>For a <c>WebKitUserMediaPermissionRequest</c>: whether it asks for a microphone (a <c>gboolean</c>).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_user_media_permission_is_for_audio_device(nint request);

    /// <summary>For a <c>WebKitUserMediaPermissionRequest</c>: whether it asks for a camera (a <c>gboolean</c>).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_user_media_permission_is_for_video_device(nint request);

    /// <summary>For a <c>WebKitUserMediaPermissionRequest</c>: whether it asks to capture a screen or window (2.34).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_user_media_permission_is_for_display_device(nint request);

    /// <summary>The feature a <c>navigator.permissions.query</c> asks about (<c>"geolocation"</c>, …); borrowed UTF-8.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_permission_state_query_get_name(nint query);

    /// <summary>Answers a permission query with a <c>WebKitPermissionState</c>: 0 granted, 1 denied, 2 prompt.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_permission_state_query_finish(nint query, int state);

    /// <summary>The view's settings; borrowed. Setters take a <c>gboolean</c>.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_web_view_get_settings(nint view);

    [LibraryImport(Library)]
    public static partial void webkit_settings_set_enable_developer_extras(nint settings, int enabled);

    [LibraryImport(Library)]
    public static partial int webkit_settings_get_enable_developer_extras(nint settings);

    [LibraryImport(Library)]
    public static partial void webkit_settings_set_javascript_can_access_clipboard(nint settings, int enabled);

    [LibraryImport(Library)]
    public static partial int webkit_settings_get_javascript_can_access_clipboard(nint settings);

    [LibraryImport(Library)]
    public static partial void webkit_settings_set_javascript_can_open_windows_automatically(nint settings, int enabled);

    [LibraryImport(Library)]
    public static partial int webkit_settings_get_javascript_can_open_windows_automatically(nint settings);

    [LibraryImport(Library)]
    public static partial void webkit_settings_set_allow_file_access_from_file_urls(nint settings, int allowed);

    [LibraryImport(Library)]
    public static partial int webkit_settings_get_allow_file_access_from_file_urls(nint settings);

    [LibraryImport(Library)]
    public static partial void webkit_settings_set_allow_universal_access_from_file_urls(nint settings, int allowed);

    [LibraryImport(Library)]
    public static partial int webkit_settings_get_allow_universal_access_from_file_urls(nint settings);

    [LibraryImport(Library)]
    public static partial void webkit_settings_set_allow_top_navigation_to_data_urls(nint settings, int allowed);

    [LibraryImport(Library)]
    public static partial int webkit_settings_get_allow_top_navigation_to_data_urls(nint settings);

    /// <summary>How many items the menu holds (a <c>guint</c>).</summary>
    [LibraryImport(Library)]
    public static partial uint webkit_context_menu_get_n_items(nint menu);

    /// <summary>The item at <paramref name="position"/>; borrowed, the menu owns it.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_context_menu_get_item_at_position(nint menu, uint position);

    /// <summary>Removes <paramref name="item"/> from the menu, which drops its reference.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_context_menu_remove(nint menu, nint item);

    /// <summary>The item's <c>WebKitContextMenuAction</c>: 14 copy, 15 cut, 16 paste, 18 select all, 31 inspect element.</summary>
    [LibraryImport(Library)]
    public static partial int webkit_context_menu_item_get_stock_action(nint item);

    /// <summary>Whether the menu is for an editable element (a <c>gboolean</c>).</summary>
    [LibraryImport(Library)]
    public static partial int webkit_hit_test_result_context_is_editable(nint result);

    /// <summary>Answers a file chooser request with no files, as if the person cancelled the picker.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_file_chooser_request_cancel(nint request);

    /// <summary>Stops the download; its <c>failed</c> signal follows with a cancellation, and no file is written.</summary>
    [LibraryImport(Library)]
    public static partial void webkit_download_cancel(nint download);

    /// <summary>The download's request; borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint webkit_download_get_request(nint download);

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
