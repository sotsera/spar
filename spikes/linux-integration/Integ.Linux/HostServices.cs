using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Integ.Linux;

/// <summary>Saved window placement (question 4).</summary>
internal sealed record Placement(int Width, int Height, int? X, int? Y, bool Maximized, string Backend, string Api);

/// <summary>Placement, printing, file pickers and the clipboard.</summary>
internal sealed partial class Host
{
    private double _printStarted;
    private double _pickStarted;
    private double _clipStarted;
#if SPAR_GTK4
    private nint _pickCancellable;
#endif

    // ---- Placement ----

    private void SavePlacement()
    {
        if (_options.SavePlacement is not { } path || _window == 0)
        {
            return;
        }

        var maximized = Gtk.gtk_window_is_maximized(_window) != 0;
#if SPAR_GTK4
        // GTK4 keeps the unmaximized size as the default size, and recommends it for saving window state.
        Gtk.gtk_window_get_default_size(_window, out var w, out var h);
        var position = X11Origin();
#else
        var (w, h) = maximized ? _normalSize : Size();
        (int X, int Y)? position = null;
        if (IsX11 && !maximized)
        {
            Gtk.gtk_window_get_position(_window, out var x, out var y);
            position = (x, y);
        }
#endif
        var placement = new Placement(w, h, position?.X, position?.Y, maximized, IsX11 ? "x11" : "wayland", _options.Gtk4 ? "gtk4" : "gtk3");
        File.WriteAllText(path, JsonSerializer.Serialize(placement, HostJson.Default.Placement));
        Log.Mark("placement-saved " + JsonSerializer.Serialize(placement, HostJson.Default.Placement) + " " + Facts());
    }

#if !SPAR_GTK4
    private (int Width, int Height) Size()
    {
        Gtk.gtk_window_get_size(_window, out var w, out var h);
        return (w, h);
    }
#endif

    private void RestorePlacement()
    {
        if (_options.RestorePlacement is not { } path || !File.Exists(path))
        {
            return;
        }

        var placement = JsonSerializer.Deserialize(File.ReadAllText(path), HostJson.Default.Placement)!;
        Gtk.gtk_window_set_default_size(_window, placement.Width, placement.Height);
        if (placement.X is { } x && placement.Y is { } y)
        {
#if SPAR_GTK4
            // No GTK4 API; the X11 workaround needs the X window, which exists only once the window is realized.
            _ = MoveWhenMappedAsync(x, y);
#else
            Gtk.gtk_window_move(_window, x, y);
#endif
        }

        if (placement.Maximized && _options.MaximizeAfterMap)
        {
            // WSLg's compositor kills a GTK4 client that asks to be maximized before its first configure (error 71).
            _ = MaximizeWhenMappedAsync();
        }
        else if (placement.Maximized)
        {
            Gtk.gtk_window_maximize(_window);
        }

        Log.Mark("placement-restored " + File.ReadAllText(path));
    }

    private async Task MaximizeWhenMappedAsync()
    {
        for (var i = 0; i < 200 && Gtk.gtk_widget_get_mapped(_window) == 0; i++)
        {
            await Task.Delay(10);
        }

        await Task.Delay(50);
        Gtk.gtk_window_maximize(_window);
        Log.Mark("maximized after map");
    }

#if SPAR_GTK4
    private async Task MoveWhenMappedAsync(int x, int y)
    {
        for (var i = 0; i < 100 && (Xid() == 0 || Gtk.gtk_widget_get_mapped(_window) == 0); i++)
        {
            await Task.Delay(10);
        }

        Move($"{x} {y}");
    }
#endif

    // ---- Printing (question 8) ----

    private unsafe void Print(string path)
    {
        var settings = Gtk.gtk_print_settings_new();
        Gtk.gtk_print_settings_set_printer(settings, "Print to File");
        Gtk.gtk_print_settings_set(settings, "output-file-format", "pdf");
        Gtk.gtk_print_settings_set(settings, "output-uri", new Uri(Path.GetFullPath(path)).AbsoluteUri);
        var operation = WebKit.webkit_print_operation_new(_webView);
        WebKit.webkit_print_operation_set_print_settings(operation, settings);
        GObject.Connect(operation, "finished", (delegate* unmanaged<nint, nint, void>)&OnPrintFinished);
        GObject.Connect(operation, "failed", (delegate* unmanaged<nint, nint, nint, void>)&OnPrintFailed);
        _printStarted = Log.Monotonic;
        WebKit.webkit_print_operation_print(operation);
        GObject.g_object_unref(settings);
        Log.Mark("print started " + path);
    }

    [UnmanagedCallersOnly]
    private static void OnPrintFinished(nint operation, nint data)
    {
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"print finished after {Log.Monotonic - Current._printStarted:F0} ms"));
        GObject.g_object_unref(operation);
    }

    [UnmanagedCallersOnly]
    private static void OnPrintFailed(nint operation, nint error, nint data) =>
        Log.Mark("print failed: " + GLib.ErrorMessage(error));

    // ---- File pickers (question 5): open, then cancel programmatically ----

    private unsafe void Pick(int cancelAfterMs)
    {
        _pickStarted = Log.Monotonic;
#if SPAR_GTK4
        var dialog = Gtk.gtk_file_dialog_new();
        Gtk.gtk_file_dialog_set_title(dialog, "Spar picker probe");
        _pickCancellable = Gio.g_cancellable_new();
        Gtk.gtk_file_dialog_open(dialog, _window, _pickCancellable, &OnFileDialogDone, 0);
        Log.Mark("pick GtkFileDialog open called");
        _ = CancelPickAsync(cancelAfterMs, dialog);
#else
        var dialog = Gtk.gtk_file_chooser_native_new("Spar picker probe", _window, 0, null, null);
        GObject.Connect(dialog, "response", (delegate* unmanaged<nint, int, nint, void>)&OnNativeResponse);
        Gtk.gtk_native_dialog_show(dialog);
        Log.Mark("pick GtkFileChooserNative shown");
        _ = CancelPickAsync(cancelAfterMs, dialog);
#endif
    }

    private async Task CancelPickAsync(int afterMs, nint dialog)
    {
        await Task.Delay(afterMs);
#if SPAR_GTK4
        Log.Mark("pick cancelling (g_cancellable_cancel)");
        Gio.g_cancellable_cancel(_pickCancellable);
        await Task.Delay(1500);
        GObject.g_object_unref(dialog);
#else
        Log.Mark("pick cancelling (gtk_native_dialog_hide)");
        Gtk.gtk_native_dialog_hide(dialog);
        await Task.Delay(1500);
        Gtk.gtk_native_dialog_destroy(dialog);
        GObject.g_object_unref(dialog);
        Log.Mark("pick dialog destroyed");
#endif
    }

#if SPAR_GTK4
    [UnmanagedCallersOnly]
    private static void OnFileDialogDone(nint dialog, nint result, nint data)
    {
        var file = Gtk.gtk_file_dialog_open_finish(dialog, result, out var error);
        var outcome = file != 0 ? "file " + GLib.TakeString(Gio.g_file_get_path(file)) : $"error code={GLib.ErrorCode(error)} {GLib.TakeError(error)}";
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"pick done after {Log.Monotonic - Current._pickStarted:F0} ms: {outcome}"));
    }
#else
    [UnmanagedCallersOnly]
    private static void OnNativeResponse(nint dialog, int response, nint data) =>
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"pick response {response} after {Log.Monotonic - Current._pickStarted:F0} ms"));
#endif

    // ---- Host clipboard (question 6) ----

    private unsafe void ClipboardSet(string text)
    {
#if SPAR_GTK4
        Gtk.gdk_clipboard_set_text(Gtk.gdk_display_get_clipboard(Gtk.gdk_display_get_default()), text);
#else
        Gtk.gtk_clipboard_set_text(Gtk.gtk_clipboard_get(69), text, -1); // GDK_SELECTION_CLIPBOARD = atom 69
#endif
        Log.Mark($"clip-set {text.Length} chars");
    }

    private unsafe void ClipboardGet()
    {
        _clipStarted = Log.Monotonic;
#if SPAR_GTK4
        Gtk.gdk_clipboard_read_text_async(Gtk.gdk_display_get_clipboard(Gtk.gdk_display_get_default()), 0, &OnClipboardRead, 0);
#else
        Gtk.gtk_clipboard_request_text(Gtk.gtk_clipboard_get(69), &OnClipboardText, 0);
#endif
    }

    private static void ClipboardResult(string? text) =>
        // The text is printed only when it is the spike's own marker; anything else is the user's and stays private.
        Log.Mark(string.Create(CultureInfo.InvariantCulture,
            $"clip-get after {Log.Monotonic - Current._clipStarted:F1} ms: {(text is null ? "(none)" : text.StartsWith("spar-clip-", StringComparison.Ordinal) ? text : $"(other text, {text.Length} chars)")}"));

#if SPAR_GTK4
    [UnmanagedCallersOnly]
    private static void OnClipboardRead(nint clipboard, nint result, nint data)
    {
        var text = Gtk.gdk_clipboard_read_text_finish(clipboard, result, out var error);
        if (text == 0)
        {
            Log.Mark("clip-get error " + GLib.TakeError(error));
            return;
        }

        ClipboardResult(GLib.TakeString(text));
    }
#else
    [UnmanagedCallersOnly]
    private static void OnClipboardText(nint clipboard, nint text, nint data) => ClipboardResult(Marshal.PtrToStringUTF8(text));
#endif
}
