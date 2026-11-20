using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// A test window's view (<see cref="BrowserSource"/>): an ephemeral network session, the InPrivate analogue (decision
/// "Step 8's shape: window tests driven by Spar's own API"; <c>spikes/window-automation-linux</c>: 0 of 10 consecutive
/// and 0 of 2 simultaneous tests saw another's storage), and a web context of its own, so the <c>spar</c> scheme it
/// registers serves this window's app only. Loop thread; reached only behind the test-host feature switch.
/// </summary>
internal static class TestViews
{
    /// <summary>A session that keeps nothing on disk, owned.</summary>
    public static nint EphemeralSession() => Testing.TestNative.webkit_network_session_new_ephemeral();

    /// <summary>A web context of the window's own, owned.</summary>
    public static nint NewContext() => Testing.TestNative.webkit_web_context_new();

    /// <summary>The view over its session, content manager and context, all construct-only properties.</summary>
    public static unsafe nint NewView(nint session, nint content, nint context)
    {
        var values = stackalloc GValue[3];
        values[0] = default;
        values[1] = default;
        values[2] = default;
        fixed (byte* sessionName = "network-session\0"u8)
        fixed (byte* contentName = "user-content-manager\0"u8)
        fixed (byte* contextName = "web-context\0"u8)
        {
            var names = stackalloc nint[3] { (nint)sessionName, (nint)contentName, (nint)contextName };
            GLib.g_value_init(&values[0], WebKit.webkit_network_session_get_type());
            GLib.g_value_set_object(&values[0], session);
            GLib.g_value_init(&values[1], WebKit.webkit_user_content_manager_get_type());
            GLib.g_value_set_object(&values[1], content);
            GLib.g_value_init(&values[2], Testing.TestNative.webkit_web_context_get_type());
            GLib.g_value_set_object(&values[2], context);
            try
            {
                return GLib.g_object_new_with_properties(WebKit.webkit_web_view_get_type(), 3, names, values);
            }
            finally
            {
                GLib.g_value_unset(&values[0]);
                GLib.g_value_unset(&values[1]);
                GLib.g_value_unset(&values[2]);
            }
        }
    }
}
