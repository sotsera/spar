using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// GTK 4's file dialogs, the drop controller the host watches drags with, and the clipboard, checked against the 4.14
/// headers of Ubuntu 24.04 (<c>gtkfiledialog.h</c>, <c>gtkdropcontrollermotion.h</c>, <c>gdkdrop.h</c>,
/// <c>gdkclipboard.h</c>) and GIO's list model. All on the loop thread.
/// </summary>
internal static unsafe partial class Gtk
{
    private const string Gio = "libgio-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Glib = "libglib-2.0.so.0";

    /// <summary><c>GTK_DIALOG_ERROR_CANCELLED</c>: the call was cancelled (the host's cancellable).</summary>
    public const int DialogErrorCancelled = 1;

    /// <summary><c>GTK_DIALOG_ERROR_DISMISSED</c>: the user closed the dialog without choosing.</summary>
    public const int DialogErrorDismissed = 2;

    /// <summary>A new <c>GtkFileDialog</c>, owned.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_file_dialog_new();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_file_dialog_set_title(nint dialog, string title);

    [LibraryImport(Library)]
    public static partial void gtk_file_dialog_set_modal(nint dialog, int modal);

    /// <summary>The dialog's filters: a <c>GListModel</c> of <c>GtkFileFilter</c>, referenced by the dialog.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_file_dialog_set_filters(nint dialog, nint filters);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_file_dialog_set_initial_name(nint dialog, string name);

    [LibraryImport(Library)]
    public static partial void gtk_file_dialog_open(nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>The chosen <c>GFile</c>, owned; 0 with an owned <c>GError</c>.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_file_dialog_open_finish(nint dialog, nint result, nint* error);

    [LibraryImport(Library)]
    public static partial void gtk_file_dialog_open_multiple(nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>A <c>GListModel</c> of the chosen <c>GFile</c>s, owned; 0 with an owned <c>GError</c>.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_file_dialog_open_multiple_finish(nint dialog, nint result, nint* error);

    [LibraryImport(Library)]
    public static partial void gtk_file_dialog_save(nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Library)]
    public static partial nint gtk_file_dialog_save_finish(nint dialog, nint result, nint* error);

    [LibraryImport(Library)]
    public static partial void gtk_file_dialog_select_folder(nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Library)]
    public static partial nint gtk_file_dialog_select_folder_finish(nint dialog, nint result, nint* error);

    /// <summary>The quark of GTK's dialog errors (<c>GTK_DIALOG_ERROR</c>).</summary>
    [LibraryImport(Library)]
    public static partial uint gtk_dialog_error_quark();

    /// <summary>A new <c>GtkFileFilter</c>, owned.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_file_filter_new();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_file_filter_set_name(nint filter, string name);

    /// <summary>A shell pattern such as <c>*.md</c>; GTK matches it case-insensitively on its own (4.12 and later).</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_file_filter_add_pattern(nint filter, string pattern);

    [LibraryImport(Library)]
    public static partial nuint gtk_file_filter_get_type();

    /// <summary>A new <c>GtkDropControllerMotion</c>, owned until added to a widget.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_drop_controller_motion_new();

    /// <summary>The <c>GdkDrop</c> of the drag over the widget, borrowed; 0 when none is.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_drop_controller_motion_get_drop(nint controller);

    /// <summary>Reads the drop's data as a value of <paramref name="type"/>, asynchronously.</summary>
    [LibraryImport(Library)]
    public static partial void gdk_drop_read_value_async(nint drop, nuint type, int priority, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>The value read, owned by the drop; 0 with an owned <c>GError</c>.</summary>
    [LibraryImport(Library)]
    public static partial nint gdk_drop_read_value_finish(nint drop, nint result, nint* error);

    /// <summary><c>GDK_TYPE_FILE_LIST</c>.</summary>
    [LibraryImport(Library)]
    public static partial nuint gdk_file_list_get_type();

    /// <summary>A <c>GSList</c> of the list's <c>GFile</c>s; the list is owned (<see cref="g_slist_free"/>), the files are borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint gdk_file_list_get_files(nint fileList);

    /// <summary>The display's clipboard, borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint gdk_display_get_clipboard(nint display);

    /// <summary>The widget's display's clipboard, borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_widget_get_clipboard(nint widget);

    /// <summary>Puts text on the clipboard, which owns it until another program takes the clipboard.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gdk_clipboard_set_text(nint clipboard, string text);

    [LibraryImport(Library)]
    public static partial void gdk_clipboard_read_text_async(nint clipboard, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>The clipboard's text, owned (<c>g_free</c>); 0 with an owned <c>GError</c>, or with none when it holds no text.</summary>
    [LibraryImport(Library)]
    public static partial nint gdk_clipboard_read_text_finish(nint clipboard, nint result, nint* error);

    /// <summary>A <c>GListStore</c> for items of <paramref name="itemType"/>, owned.</summary>
    [LibraryImport(Gio)]
    public static partial nint g_list_store_new(nuint itemType);

    /// <summary>Appends an item, which the store references.</summary>
    [LibraryImport(Gio)]
    public static partial void g_list_store_append(nint store, nint item);

    [LibraryImport(Gio)]
    public static partial uint g_list_model_get_n_items(nint list);

    /// <summary>The item at <paramref name="position"/>, owned.</summary>
    [LibraryImport(Gio)]
    public static partial nint g_list_model_get_item(nint list, uint position);

    /// <summary>The file's local path, owned (<c>g_free</c>); 0 for a file with none (a remote location).</summary>
    [LibraryImport(Gio)]
    public static partial nint g_file_get_path(nint file);

    /// <summary>The boxed pointer a <c>GValue</c> holds, borrowed.</summary>
    [LibraryImport(GObject)]
    public static partial nint g_value_get_boxed(nint value);

    [LibraryImport(Glib)]
    public static partial void g_slist_free(nint list);
}
