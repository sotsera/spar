using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Spar.Hosting;
using Spar.Linux;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>
/// The Linux host's pure parts, which run on every OS: the app's origin on <c>spar://app</c>, the headers every scheme
/// response carries, the script literals that carry the host's messages to the relay, and the security defaults'
/// decisions (the environment filter, the context menu). The host itself, in a real
/// window, is <see cref="LinuxHostTests"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHostUnitTests
{
    [Theory]
    [InlineData("spar://app/", true)]
    [InlineData("spar://app/assets/index.js?v=1", true)]
    [InlineData("spar://app", false)]
    [InlineData("spar://app:1/", false)]
    [InlineData("spar://apps/", false)]
    [InlineData("spar://other/", false)]
    [InlineData("https://app/", false)]
    [InlineData("about:blank", false)]
    public void Only_the_apps_own_origin_counts(string address, bool own)
    {
        Assert.Equal(own, SchemeServer.IsAppOrigin(address));
    }

    [Theory]
    [InlineData("spar://app/", "/")]
    [InlineData("spar://app/other.html?x=1#top", "/other.html")]
    [InlineData("spar://app/a%20b/caf%C3%A9.css", "/a b/café.css")]
    public void A_path_is_decoded_without_its_query_and_fragment(string address, string path)
    {
        Assert.Equal(path, PageResponder.PathOf(address, SchemeServer.Origin));
    }

    [Fact]
    public void Every_response_repeats_its_type_in_its_headers_and_carries_the_policy_for_spar_app()
    {
        var responder = new PageResponder(null, SparPageSecurity.Default, SchemeServer.Origin, development: false);
        var response = new PageResponse(200, "OK", "text/javascript; charset=utf-8", "x"u8.ToArray());

        var headers = SchemeServer.HeadersOf(response, responder).ToList();

        Assert.Equal(new("Content-Type", "text/javascript; charset=utf-8"), headers[0]);
        var policy = Assert.Single(headers, header => header.Key == "Content-Security-Policy").Value;
        Assert.Contains("connect-src spar://app", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'self' ws", policy, StringComparison.Ordinal);
        Assert.Contains(headers, header => header is { Key: "X-Content-Type-Options", Value: "nosniff" });
        Assert.Contains(headers, header => header is { Key: "Cache-Control", Value: "no-store" });
        Assert.DoesNotContain(headers, header => header.Key == "Allow");
        Assert.Equal(headers.Count, headers.Select(header => header.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_refused_method_says_which_ones_are_allowed_and_nothing_is_a_redirect()
    {
        var responder = new PageResponder(null, SparPageSecurity.Default, SchemeServer.Origin, development: false);

        var refused = responder.Answer("/", "POST", _ => { });
        var missing = responder.Answer("/", "GET", _ => { });

        Assert.Equal(405, refused.Status);
        Assert.Contains(SchemeServer.HeadersOf(refused, responder), header => header is { Key: "Allow", Value: "GET, HEAD" });
        Assert.Equal(404, missing.Status);
        Assert.Equal(PageResponder.NoPage, Encoding.UTF8.GetString(missing.Body.Span));
    }

    [Theory]
    [InlineData("{\"type\":\"open\",\"protocol\":1}\t")]
    [InlineData("{\"type\":\"result\",\"id\":7,\"ok\":true}\t\"a \\\"quoted\\\" \\\\ value\"")]
    [InlineData("line\nbreak\rand\ttab\u0000nul")]
    [InlineData("separators \u2028 and \u2029, emoji \U0001F600, accents \u00e0\u00e9")]
    [InlineData("")]
    public void A_host_message_becomes_a_script_literal_that_reads_back_exactly(string message)
    {
        var literal = ScriptBridge.Literal(message);
        var text = Encoding.UTF8.GetString(literal);

        // A JavaScript string literal of this form is also a JSON string: read it back with a JSON reader.
        Assert.EndsWith(",", text, StringComparison.Ordinal);
        var reader = new Utf8JsonReader(literal.AsSpan(0, literal.Length - 1));
        Assert.True(reader.Read());
        Assert.Equal(message, reader.GetString());
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain((char)0x2028, text);
        Assert.DoesNotContain((char)0x2029, text);
    }

    [Fact]
    public void A_lone_surrogate_is_escaped_so_the_script_stays_valid_UTF_8()
    {
        var literal = ScriptBridge.Literal("a\ud800b\udc00c");

        Assert.Equal("\"a\\ud800b\\udc00c\",", Encoding.UTF8.GetString(literal));
        Assert.Equal(literal, new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(Encoding.UTF8.GetString(literal)));
    }

    [Fact]
    public void A_burst_of_messages_is_one_script_for_the_relay_in_order()
    {
        var script = ScriptBridge.DeliverScript([ScriptBridge.Literal("one"), ScriptBridge.Literal("two")]);

        Assert.Equal(
            "typeof sparRelay === \"object\" && sparRelay.deliver([\"one\",\"two\",]);",
            Encoding.UTF8.GetString(script));
    }

    [Fact]
    public void The_relay_runs_in_its_own_world_and_defines_nothing_on_the_page()
    {
        var relay = ScriptBridge.RelayScript(reportViolations: true);

        Assert.Contains("const sparRelay = (() => {", relay, StringComparison.Ordinal);
        Assert.Contains("location.protocol === \"spar:\" && location.host === \"app\"", relay, StringComparison.Ordinal);
        Assert.DoesNotContain("window.sparRelay", relay, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", relay, StringComparison.Ordinal);
        Assert.DoesNotContain("Function(", relay, StringComparison.Ordinal);
        Assert.DoesNotContain("__SPAR_", relay, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"securitypolicyviolation\", onViolation, true)", relay, StringComparison.Ordinal);
        Assert.Contains("if (false)", ScriptBridge.RelayScript(reportViolations: false), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("spar://app/other.html", true)]
    [InlineData("about:srcdoc", false)]
    [InlineData("about:blank", false)]
    [InlineData("http://127.0.0.1:5000/frame.html", true)]
    [InlineData("http://127.0.0.1/frame.html", true)]
    [InlineData("https://127.0.0.1:5000/frame.html", false)]
    [InlineData("http://localhost:5000/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("spar://other/", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("blob:spar://app/0a1b", false)]
    [InlineData("file:///etc/hostname", false)]
    [InlineData("javascript:alert(1)", false)]
    public void The_lock_lets_through_the_apps_origin_and_the_declared_frames_only(string address, bool allowed)
    {
        Assert.Equal(allowed, PageGuard.Allows(address, ["http://127.0.0.1:*"]));
    }

    [Theory]
    [InlineData("https://frames.example/a", "https://frames.example", true)]
    [InlineData("https://frames.example:443/a", "https://frames.example", true)]
    [InlineData("https://frames.example:8443/a", "https://frames.example", false)]
    [InlineData("http://frames.example/a", "https://frames.example", false)]
    [InlineData("https://FRAMES.example/", "https://frames.example", true)]
    [InlineData("https://a.frames.example/", "https://*.frames.example", true)]
    [InlineData("https://frames.example/", "https://*.frames.example", false)]
    [InlineData("https://evilframes.example/", "https://*.frames.example", false)]
    [InlineData("https://any.example/x", "https:", true)]
    [InlineData("http://any.example/x", "https:", false)]
    [InlineData("http://frames.example/", "frames.example", true)]
    [InlineData("https://frames.example/", "frames.example", true)]
    [InlineData("https://frames.example/other/page", "https://frames.example/embed/", true)]
    [InlineData("https://user@frames.example/", "https://frames.example", false)]
    [InlineData("spar://app/inner.html", "'self'", true)]
    [InlineData("https://app/inner.html", "'self'", false)]
    public void A_declared_frame_source_matches_its_scheme_host_and_port(string address, string source, bool matches)
    {
        Assert.Equal(matches, NavigationRules.IsDeclaredFrame(address, [source], SchemeServer.Origin));
    }

    [Theory]
    [InlineData("https://example.com/docs", null)]
    [InlineData("mailto:someone@example.com", null)]
    [InlineData("spar://app/settings", "not 'spar:'")]
    [InlineData("https://user:secret@example.com/", "credentials")]
    [InlineData("file:///etc/passwd", "not 'file:'")]
    public void A_users_click_opens_only_plain_external_addresses_on_Linux_too(string address, string? refusal)
    {
        var reason = NavigationRules.ExternalRefusal(address, SchemeServer.Origin);

        if (refusal is null)
        {
            Assert.Null(reason);
        }
        else
        {
            Assert.Contains(refusal, reason, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("WebKitGeolocationPermissionRequest", false, false, false, "Geolocation", true)]
    [InlineData("WebKitGeolocationPermissionRequest", false, false, false, "", false)]
    [InlineData("WebKitUserMediaPermissionRequest", false, true, false, "Camera", true)]
    [InlineData("WebKitUserMediaPermissionRequest", true, false, false, "Camera", false)]
    [InlineData("WebKitUserMediaPermissionRequest", true, false, false, "Microphone", true)]
    [InlineData("WebKitUserMediaPermissionRequest", true, true, false, "Camera", false)]
    [InlineData("WebKitUserMediaPermissionRequest", true, true, false, "Camera,Microphone", true)]
    [InlineData("WebKitUserMediaPermissionRequest", false, true, true, "Camera,Microphone", false)]
    [InlineData("WebKitUserMediaPermissionRequest", false, false, false, "Camera,Microphone", false)]
    [InlineData("WebKitClipboardPermissionRequest", false, false, false, "ClipboardRead", true)]
    [InlineData("WebKitClipboardPermissionRequest", false, false, false, "Geolocation", false)]
    [InlineData("WebKitDeviceInfoPermissionRequest", false, false, false, "Microphone", true)]
    [InlineData("WebKitDeviceInfoPermissionRequest", false, false, false, "Geolocation", false)]
    [InlineData("WebKitNotificationPermissionRequest", false, false, false, "Camera,Microphone,Geolocation,ClipboardRead", false)]
    [InlineData("WebKitPointerLockPermissionRequest", false, false, false, "Camera,Microphone,Geolocation,ClipboardRead", false)]
    [InlineData("WebKitMediaKeySystemPermissionRequest", false, false, false, "Camera,Microphone,Geolocation,ClipboardRead", false)]
    [InlineData("WebKitWebsiteDataAccessPermissionRequest", false, false, false, "Camera,Microphone,Geolocation,ClipboardRead", false)]
    [InlineData("WebKitXRPermissionRequest", false, false, false, "Camera,Microphone,Geolocation,ClipboardRead", false)]
    [InlineData("WebKitSomethingNewPermissionRequest", false, false, false, "Camera,Microphone,Geolocation,ClipboardRead", false)]
    public void A_permission_request_is_granted_only_for_what_the_app_declared(
        string type, bool audio, bool video, bool display, string declared, bool granted)
    {
        var permissions = declared.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<Permission>).ToArray();

        Assert.Equal(granted, PageGuard.Decide(type, audio, video, display, permissions).Granted);
    }

    [Fact]
    public void A_permission_query_says_denied_for_everything_not_declared()
    {
        Permission[] declared = [Permission.Geolocation];

        Assert.Equal(2, PageGuard.QueryState("geolocation", declared));
        Assert.Equal(1, PageGuard.QueryState("camera", declared));
        Assert.Equal(1, PageGuard.QueryState("notifications", declared));
        Assert.Equal(1, PageGuard.QueryState("midi", declared));
    }

    [Fact]
    public void The_browser_profile_folder_is_named_after_the_engine_and_the_cache_sits_beside_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "com.example.SqlTool");
        var folders = SparFolders.Under(root, new FolderOptions());

        Assert.Equal(Path.Combine(root, OperatingSystem.IsWindows() ? "WebView2" : "WebKit"), folders.Profile);
        Assert.Equal(Path.Combine(root, "cache", "WebKit"), AppWindow.CacheFolder(folders));
        Assert.Equal("/elsewhere/profile", SparFolders.Under(root, new FolderOptions().Profile("/elsewhere/profile")).Profile);
    }

    [Theory]
    [InlineData(false, null, "WebAssembly off: JSC_useWasm=false.")]
    [InlineData(false, "false", "WebAssembly off: JSC_useWasm=false.")]
    [InlineData(false, "1", "WebAssembly off: JSC_useWasm=false, replacing the inherited JSC_useWasm=1.")]
    [InlineData(true, null, "WebAssembly on (AllowWebAssembly()): JSC_useWasm=true.")]
    [InlineData(true, "false", "WebAssembly on (AllowWebAssembly()): JSC_useWasm=true, replacing the inherited JSC_useWasm=false.")]
    public void The_engines_WebAssembly_setting_follows_the_declaration_whatever_the_environment_held(bool allowed, string? inherited, string line)
    {
        Assert.Equal(allowed ? "true" : "false", EngineEnvironment.Value(allowed));
        Assert.Equal(line, EngineEnvironment.Describe(allowed, inherited));
    }

    [Fact]
    public void The_environment_overrides_Spar_removes_on_Linux_are_exactly_the_documented_ones()
    {
        Assert.Equal(
            ["WEBKIT_INSPECTOR_SERVER", "WEBKIT_INSPECTOR_HTTP_SERVER", "WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS"],
            EngineEnvironment.Overrides());
    }

    /// <summary>
    /// The environment filter (plan section 3c; user decision 2026-11-20): the three overrides go unless allowed, in
    /// every build; JavaScriptCore's options and GTK_DEBUG go from a Production build unless allowed; JSC_useWasm is
    /// never the filter's (it is always written); every other variable passes, the GPU workarounds and GTK's rendering
    /// choices (GSK_RENDERER, GDK_DEBUG) in particular.
    /// </summary>
    [Theory]
    [InlineData(false, false, "WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER JSC_useDollarVM WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS GTK_DEBUG JSC_dumpOptions", "")]
    [InlineData(false, true, "WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "JSC_useDollarVM GTK_DEBUG JSC_dumpOptions")]
    [InlineData(true, false, "", "WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER JSC_useDollarVM WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS GTK_DEBUG JSC_dumpOptions")]
    [InlineData(true, true, "", "WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER JSC_useDollarVM WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS GTK_DEBUG JSC_dumpOptions")]
    public void The_environment_filter_removes_the_overrides_and_Productions_JavaScriptCore_options_only(
        bool allowed, bool development, string removed, string kept)
    {
        string[] environment =
        [
            "PATH", "WEBKIT_INSPECTOR_SERVER", "JSC_useWasm", "__NV_DISABLE_EXPLICIT_SYNC", "WEBKIT_DISABLE_DMABUF_RENDERER",
            "WEBKIT_DISABLE_COMPOSITING_MODE", "GSK_RENDERER", "WEBKIT_INSPECTOR_HTTP_SERVER", "JSC_useDollarVM",
            "WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "GTK_DEBUG", "gtk_debug", "GDK_DEBUG", "jsc_lower", "JSC_dumpOptions",
            "WEBKIT_INSPECTOR_SERVER", "WEBKIT_DEBUG",
        ];

        var decided = EngineEnvironment.Decide(environment, allowed, development);

        Assert.Equal(removed, string.Join(' ', decided.Where(d => d.Remove).Select(d => d.Name)));
        Assert.Equal(kept, string.Join(' ', decided.Where(d => !d.Remove).Select(d => d.Name)));
        Assert.All(decided, d => Assert.StartsWith(d.Name + (d.Remove ? " ignored" : " in force"), d.Line, StringComparison.Ordinal));
        Assert.All(decided.Where(d => d.Remove || allowed), d => Assert.Contains("AllowBrowserEnvironmentOverrides", d.Line, StringComparison.Ordinal));
        if (development && !allowed)
        {
            Assert.Contains(decided, d => d.Line == "JSC_useDollarVM in force (a Development build keeps JavaScriptCore options): in this app and its web processes.");
        }
    }

    [Theory]
    [InlineData(15, true, false, true)] // cut
    [InlineData(14, true, false, true)] // copy
    [InlineData(16, true, false, true)] // paste
    [InlineData(18, true, false, true)] // select all
    [InlineData(14, false, false, false)] // copy of plain text
    [InlineData(17, true, false, false)] // delete
    [InlineData(19, true, false, false)] // input methods
    [InlineData(0, true, false, false)] // a separator or the app's own item
    [InlineData(10, false, false, false)] // back
    [InlineData(13, false, false, false)] // reload
    [InlineData(3, false, false, false)] // download the link
    [InlineData(31, false, false, false)] // inspect element
    [InlineData(31, false, true, true)]
    [InlineData(31, true, true, true)]
    public void The_Linux_context_menu_keeps_only_editing_items_and_Inspect_where_devtools_are_on(int action, bool editable, bool devTools, bool kept) =>
        Assert.Equal(kept, BrowserDefaults.KeptAction(action, editable, devTools));

    [Theory]
    [InlineData(0x49u, 0b101u, true)] // Ctrl+Shift+I
    [InlineData(0x69u, 0b101u, true)] // Ctrl+Shift+i
    [InlineData(0x44u, 0b101u, true)] // Ctrl+Shift+D
    [InlineData(0x64u, 0b101u, true)] // Ctrl+Shift+d
    [InlineData(0x49u, 0b1101u, true)] // Ctrl+Shift+Alt+I, stopped too: the page has had it already
    [InlineData(0x49u, 0b100u, false)] // Ctrl+I is the page's
    [InlineData(0x49u, 0b001u, false)] // Shift+I is typing
    [InlineData(0x4Au, 0b101u, false)] // Ctrl+Shift+J
    [InlineData(0x52u, 0b101u, false)] // Ctrl+Shift+R
    public void Only_GTKs_inspector_keys_are_stopped(uint keyval, uint state, bool stopped) =>
        Assert.Equal(stopped, BrowserDefaults.IsGtkInspectorKey(keyval, state));
}
