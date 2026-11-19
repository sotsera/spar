using System.Runtime.InteropServices;

namespace Spar.Spikes.GpuProbe;

internal static unsafe partial class GtkNative
{
    private const string Gtk = "libgtk-4.so.1";

    [LibraryImport(Gtk)]
    public static partial int gtk_init_check();

    [LibraryImport(Gtk)]
    public static partial uint gtk_get_major_version();

    [LibraryImport(Gtk)]
    public static partial uint gtk_get_minor_version();

    [LibraryImport(Gtk)]
    public static partial uint gtk_get_micro_version();

    [LibraryImport(Gtk)]
    public static partial nint gdk_display_get_default();

    [LibraryImport(Gtk)]
    public static partial nint gdk_display_create_gl_context(nint display, nint* error);

    [LibraryImport(Gtk)]
    public static partial int gdk_gl_context_realize(nint context, nint* error);

    [LibraryImport(Gtk)]
    public static partial void gdk_gl_context_make_current(nint context);

    [LibraryImport(Gtk)]
    public static partial void gdk_gl_context_clear_current();

    [LibraryImport(Gtk)]
    public static partial int gdk_gl_context_get_use_es(nint context);

    [LibraryImport(Gtk)]
    public static partial nint gtk_window_new();

    [LibraryImport(Gtk)]
    public static partial void gtk_widget_realize(nint widget);

    [LibraryImport(Gtk)]
    public static partial nint gtk_native_get_renderer(nint native);

    [LibraryImport(Gtk)]
    public static partial void gtk_window_destroy(nint window);

    [LibraryImport("libgobject-2.0.so.0")]
    public static partial void g_object_unref(nint obj);

    [LibraryImport("libgobject-2.0.so.0")]
    public static partial nint g_type_name_from_instance(nint instance);

    [LibraryImport("libglib-2.0.so.0")]
    public static partial void g_error_free(nint error);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int setenv(string name, string value, int overwrite);

    /// <summary>GError: GQuark domain (uint32), gint code, gchar* message at offset 8.</summary>
    public static string TakeError(nint error)
    {
        if (error == 0)
        {
            return "(no GError)";
        }
        var message = Marshal.PtrToStringUTF8(*(nint*)(error + 8)) ?? "";
        g_error_free(error);
        return message;
    }
}

internal sealed class GdkResult
{
    public string? GtkVersion;
    public bool Initialized;
    public int ThreadsBeforeInit;
    public int ThreadsAfterInit;
    public bool? UseEs;
    public string? GlVendor;
    public string? GlRenderer;
    public string? GlVersion;
    public string? WindowRenderer;
    public string? Error;
    public readonly List<(string Name, double Ms)> Phases = [];
}

/// <summary>
/// GDK's own signal: gtk_init_check, then a GL context from the default display (no window, no surface), made current
/// to read GL_RENDERER. Optionally realizes (never shows) a window to see which GSK renderer GTK picked.
/// </summary>
internal static unsafe class GdkProbe
{
    public static GdkResult Run(bool window, string? gskAfterInit, Phase phase)
    {
        var r = new GdkResult { ThreadsBeforeInit = Threads() };
        try
        {
            if (GtkNative.gtk_init_check() == 0)
            {
                r.Error = "gtk_init_check failed (no display?)";
                return r;
            }
            r.Initialized = true;
            r.ThreadsAfterInit = Threads();
            r.GtkVersion = $"{GtkNative.gtk_get_major_version()}.{GtkNative.gtk_get_minor_version()}.{GtkNative.gtk_get_micro_version()}";
            phase.Mark(r.Phases, "gtk-init");

            var display = GtkNative.gdk_display_get_default();
            nint error = 0;
            var ctx = GtkNative.gdk_display_create_gl_context(display, &error);
            if (ctx == 0)
            {
                r.Error = "create_gl_context: " + GtkNative.TakeError(error);
            }
            else
            {
                if (GtkNative.gdk_gl_context_realize(ctx, &error) == 0)
                {
                    r.Error = "gl realize: " + GtkNative.TakeError(error);
                }
                else
                {
                    GtkNative.gdk_gl_context_make_current(ctx);
                    r.UseEs = GtkNative.gdk_gl_context_get_use_es(ctx) != 0;
                    var getString = GlGetString();
                    if (getString != null)
                    {
                        r.GlVendor = EglNative.Str(getString(EglNative.GL_VENDOR));
                        r.GlRenderer = EglNative.Str(getString(EglNative.GL_RENDERER));
                        r.GlVersion = EglNative.Str(getString(EglNative.GL_VERSION));
                    }
                    GtkNative.gdk_gl_context_clear_current();
                }
                GtkNative.g_object_unref(ctx);
            }
            phase.Mark(r.Phases, "gl-context-and-strings");

            if (window)
            {
                if (gskAfterInit is not null)
                {
                    // Deliberately after gtk_init: does GSK still read the variable when the first window realizes?
                    GtkNative.setenv("GSK_RENDERER", gskAfterInit, 1);
                }
                var w = GtkNative.gtk_window_new();
                GtkNative.gtk_widget_realize(w);
                var renderer = GtkNative.gtk_native_get_renderer(w);
                r.WindowRenderer = renderer == 0 ? "(none)" : EglNative.Str(GtkNative.g_type_name_from_instance(renderer));
                GtkNative.gtk_window_destroy(w);
                phase.Mark(r.Phases, "window-realize");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            r.Error = ex.GetType().Name + ": " + ex.Message;
        }
        return r;
    }

    // glvnd's libOpenGL dispatches to whichever context is current (GDK uses EGL or GLX on X11).
    private static delegate* unmanaged<uint, nint> GlGetString()
    {
        foreach (var lib in new[] { "libOpenGL.so.0", "libGL.so.1", "libGLESv2.so.2" })
        {
            if (NativeLibrary.TryLoad(lib, out var h) && NativeLibrary.TryGetExport(h, "glGetString", out var f))
            {
                return (delegate* unmanaged<uint, nint>)f;
            }
        }
        return null;
    }

    public static int Threads()
    {
        try
        {
            return Directory.GetDirectories("/proc/self/task").Length;
        }
        catch (IOException)
        {
            return -1;
        }
    }
}
