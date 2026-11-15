using System.Runtime.InteropServices;

// Additions to the linked declarations of spikes/linux-desktop (WebKitGTK 6.0 headers, 2.52.6).
namespace Linux.Desktop;

internal static partial class WebKit
{
    /// <summary>2.40+: script messages whose promise resolves with the host's reply.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler_with_reply(nint manager, string name, string? worldName);

    [LibraryImport(Lib)]
    public static partial nint webkit_script_message_reply_ref(nint reply);

    [LibraryImport(Lib)]
    public static partial void webkit_script_message_reply_unref(nint reply);

    [LibraryImport(Lib)]
    public static partial void webkit_script_message_reply_return_value(nint reply, nint value);
}

internal static partial class Jsc
{
    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint jsc_value_get_context(nint value);

    /// <summary>Owned.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint jsc_value_new_string(nint context, string text);
}
