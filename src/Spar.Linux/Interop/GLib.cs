using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// GLib, GObject and GIO, as the host uses them (checked against the 2.80 headers of Ubuntu 24.04). The libraries load
/// at the first call; plan section 3's start-up check finds a missing one first (M1c step 2). Ownership follows each
/// function's transfer annotation, noted where it matters: <c>gboolean</c> and <c>guint</c> are 32-bit, <c>gsize</c>,
/// <c>gulong</c> and <c>GType</c> pointer-sized (LP64).
/// </summary>
internal static unsafe partial class GLib
{
    /// <summary><c>G_PRIORITY_DEFAULT</c>: posted work runs before redraws (120) and idle work (200).</summary>
    public const int PriorityDefault = 0;

    /// <summary><c>G_SOURCE_REMOVE</c>: a source function's answer that it runs once.</summary>
    public const int SourceRemove = 0;

    /// <summary><c>G_TYPE_STRING</c>, a fundamental type: 16 shifted by <c>G_TYPE_FUNDAMENTAL_SHIFT</c> (2).</summary>
    public const nuint TypeString = 16 << 2;

    private const string Glib = "libglib-2.0.so.0";
    private const string Gobject = "libgobject-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";

    /// <summary>A loop on the default main context (<paramref name="context"/> 0); owned: <see cref="g_main_loop_unref"/>.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_main_loop_new(nint context, int isRunning);

    [LibraryImport(Glib)]
    public static partial void g_main_loop_run(nint loop);

    /// <summary>Safe from any thread: the loop's context is woken.</summary>
    [LibraryImport(Glib)]
    public static partial void g_main_loop_quit(nint loop);

    [LibraryImport(Glib)]
    public static partial void g_main_loop_unref(nint loop);

    /// <summary>Adds a one-shot source to the default context from any thread, and wakes it.</summary>
    [LibraryImport(Glib)]
    public static partial uint g_idle_add_full(int priority, delegate* unmanaged<nint, int> function, nint data, nint notify);

    [LibraryImport(Glib)]
    public static partial void g_free(nint memory);

    /// <summary>Frees a <c>GError</c> the caller owns (every <c>GError**</c> out-parameter that was set).</summary>
    [LibraryImport(Glib)]
    public static partial void g_error_free(nint error);

    /// <summary>Bytes over memory that outlives every reader (the executable's image); owned, <see cref="g_bytes_unref"/>.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_bytes_new_static(void* data, nuint size);

    /// <summary>Bytes over a copy of <paramref name="data"/>; owned, <see cref="g_bytes_unref"/>.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Glib)]
    public static partial void g_bytes_unref(nint bytes);

    /// <summary>The program's name, which GTK makes the window's class and app id (without it: "Unnamed Window").</summary>
    [LibraryImport(Glib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_set_prgname(string name);

    /// <summary>
    /// Connects a signal handler; <paramref name="data"/> is borrowed and outlives the instance or the disconnection.
    /// Returns the handler id (a <c>gulong</c>).
    /// </summary>
    [LibraryImport(Gobject, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint g_signal_connect_data(
        nint instance, string detailedSignal, void* handler, nint data, nint destroyData, int flags);

    [LibraryImport(Gobject)]
    public static partial void g_signal_handler_disconnect(nint instance, nuint handlerId);

    [LibraryImport(Gobject)]
    public static partial nint g_object_ref(nint instance);

    [LibraryImport(Gobject)]
    public static partial void g_object_unref(nint instance);

    /// <summary>
    /// Creates an object with construct properties, the non-variadic <c>g_object_new</c>. A widget comes back with a
    /// floating reference, which its parent sinks; anything else with one owned reference.
    /// </summary>
    [LibraryImport(Gobject)]
    public static partial nint g_object_new_with_properties(nuint type, uint count, nint* names, GValue* values);

    [LibraryImport(Gobject)]
    public static partial GValue* g_value_init(GValue* value, nuint type);

    /// <summary>Takes its own reference on <paramref name="instance"/>; <see cref="g_value_unset"/> drops it.</summary>
    [LibraryImport(Gobject)]
    public static partial void g_value_set_object(GValue* value, nint instance);

    [LibraryImport(Gobject)]
    public static partial void g_value_unset(GValue* value);

    /// <summary>An input stream over <paramref name="bytes"/>, which it references; owned, <see cref="g_object_unref"/>.</summary>
    [LibraryImport(Gio)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

    /// <summary>The message of a <c>GError</c> (its third field, after the 32-bit domain and code), copied.</summary>
    public static string ErrorMessage(nint error) =>
        error == 0 ? "" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "";

    /// <summary>Takes an owned UTF-8 string: copies it and frees the native one.</summary>
    public static string? TakeString(nint text)
    {
        if (text == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            g_free(text);
        }
    }
}

/// <summary>A <c>GValue</c>: its type, then two 64-bit data words; zeroed before <see cref="GLib.g_value_init"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    public nuint Type;
    public ulong Data0;
    public ulong Data1;
}

/// <summary>A <c>GdkRGBA</c>: four floats from 0 to 1.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRgba
{
    public float Red;
    public float Green;
    public float Blue;
    public float Alpha;
}
