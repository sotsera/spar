using System.Runtime.InteropServices;
using Spar.Files;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window's file dialogs (decision "M3c's decide-first questions", item 4, and "The drop design, from the
/// file-drop spike", item 4): GTK 4's <c>GtkFileDialog</c>, modal over the window, which goes through the XDG
/// file-chooser portal. Without the portal GTK falls back to an in-process chooser of its own (the spike, in Spar's CI
/// image), so the host first asks the session bus for <c>org.freedesktop.portal.FileChooser</c>, off the loop, and
/// answers <see cref="FileDialogError.Unavailable"/> with the reason when it is not there. The dialog runs without a
/// nested main loop; its answer arrives on the loop. A cancelled call, or the window closing, cancels the open dialog.
/// </summary>
internal sealed class FileDialogHost(MainLoop loop, Func<nint> window, HostLog log) : INativeFileDialogs
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string FileChooser = "org.freedesktop.portal.FileChooser";

    /// <summary>How long the portal check waits; the bus may start the portal for it.</summary>
    private const int PortalMilliseconds = 10_000;

    private Pending? _open;

    public async Task<Result<IReadOnlyList<string>, FileDialogError>> ShowAsync(FileDialogRequest request, CancellationToken cancellationToken)
    {
        if (await Task.Run(FindPortal, cancellationToken) is { } missing)
        {
            log.Warn($"No file dialog: {missing}");
            return new FileDialogError.Unavailable(missing);
        }

        var pending = new Pending(request.Kind);
        loop.Post(() => Start(pending, request), "file dialog");
        await using var cancel = cancellationToken.Register(() => loop.Post(pending.Cancel, "file dialog cancelled"));
        return await pending.Answer.Task;
    }

    /// <summary>Cancels the open dialog (loop thread), as the window closes; its answer arrives on a later turn.</summary>
    public void CloseOpen() => _open?.Cancel();

    /// <summary>Whether a dialog's answer is still to come, so the window waits for it before it is destroyed.</summary>
    public Task Closed => _open?.Answer.Task ?? Task.CompletedTask;

    /// <summary>Why no file-chooser portal answers, or null when one does (any thread but the loop's: it waits).</summary>
    private static unsafe string? FindPortal()
    {
        nint error = 0;
        var bus = GLib.g_bus_get_sync(GLib.BusSession, 0, &error);
        if (bus == 0)
        {
            var message = GLib.ErrorMessage(error);
            GLib.g_error_free(error);
            return $"there is no session bus, so no file-chooser portal ({message}).";
        }

        try
        {
            var children = stackalloc nint[2];
            children[0] = GLib.g_variant_new_string(FileChooser);
            children[1] = GLib.g_variant_new_string("version");
            var parameters = GLib.g_variant_new_tuple(children, 2);
            var result = GLib.g_dbus_connection_call_sync(
                bus, PortalService, PortalPath, "org.freedesktop.DBus.Properties", "Get", parameters, 0, 0, PortalMilliseconds, 0, &error);
            if (result == 0)
            {
                var message = GLib.ErrorMessage(error);
                GLib.g_error_free(error);
                return $"no file-chooser portal ({FileChooser}) answers on the session bus ({message}). Install xdg-desktop-portal with a backend for the desktop, such as xdg-desktop-portal-gtk.";
            }

            GLib.g_variant_unref(result);
            return null;
        }
        finally
        {
            GLib.g_object_unref(bus);
        }
    }

    private unsafe void Start(Pending pending, FileDialogRequest request)
    {
        var dialog = Gtk.gtk_file_dialog_new();
        Gtk.gtk_file_dialog_set_modal(dialog, 1);
        if (request.Title is { } title)
        {
            Gtk.gtk_file_dialog_set_title(dialog, title);
        }

        if (request.Kind != FileDialogKind.Folder && request.Filters.Count > 0)
        {
            SetFilters(dialog, request.Filters);
        }

        if (request.Kind == FileDialogKind.Save && request.SuggestedName is { } name)
        {
            Gtk.gtk_file_dialog_set_initial_name(dialog, name);
        }

        pending.Cancellable = GLib.g_cancellable_new();
        _open = pending;
        var state = GCHandle.ToIntPtr(GCHandle.Alloc(pending));
        var parent = window();
        switch (request.Kind)
        {
            case FileDialogKind.Open:
                Gtk.gtk_file_dialog_open(dialog, parent, pending.Cancellable, &Answered, state);
                break;
            case FileDialogKind.OpenMany:
                Gtk.gtk_file_dialog_open_multiple(dialog, parent, pending.Cancellable, &Answered, state);
                break;
            case FileDialogKind.Save:
                Gtk.gtk_file_dialog_save(dialog, parent, pending.Cancellable, &Answered, state);
                break;
            default:
                Gtk.gtk_file_dialog_select_folder(dialog, parent, pending.Cancellable, &Answered, state);
                break;
        }

        pending.Done = () =>
        {
            if (_open == pending)
            {
                _open = null;
            }
        };
    }

    /// <summary>The filters as a list of <c>GtkFileFilter</c>s, one pattern each from <c>*.md;*.markdown</c>.</summary>
    private static void SetFilters(nint dialog, IReadOnlyList<FileFilter> filters)
    {
        var store = Gtk.g_list_store_new(Gtk.gtk_file_filter_get_type());
        foreach (var filter in filters)
        {
            var gtkFilter = Gtk.gtk_file_filter_new();
            Gtk.gtk_file_filter_set_name(gtkFilter, filter.Name);
            foreach (var pattern in filter.Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Gtk.gtk_file_filter_add_pattern(gtkFilter, pattern);
            }

            Gtk.g_list_store_append(store, gtkFilter);
            GLib.g_object_unref(gtkFilter);
        }

        Gtk.gtk_file_dialog_set_filters(dialog, store);
        GLib.g_object_unref(store);
    }

    /// <summary>The dialog's answer, on the loop: the dialog (owned since its creation) and its cancellable are released here.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void Answered(nint dialog, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var pending = (Pending)handle.Target!;
        handle.Free();
        try
        {
            nint error = 0;
            pending.Answer.TrySetResult(pending.Kind switch
            {
                FileDialogKind.Open => One(Gtk.gtk_file_dialog_open_finish(dialog, result, &error), error),
                FileDialogKind.OpenMany => Many(Gtk.gtk_file_dialog_open_multiple_finish(dialog, result, &error), error),
                FileDialogKind.Save => One(Gtk.gtk_file_dialog_save_finish(dialog, result, &error), error),
                _ => One(Gtk.gtk_file_dialog_select_folder_finish(dialog, result, &error), error),
            });
        }
        catch (Exception exception)
        {
            pending.Answer.TrySetException(exception);
        }
        finally
        {
            GLib.g_object_unref(dialog);
            GLib.g_object_unref(pending.Cancellable);
            pending.Cancellable = 0;
            pending.Done?.Invoke();
        }
    }

    private static Result<IReadOnlyList<string>, FileDialogError> One(nint file, nint error)
    {
        if (file == 0)
        {
            return Failure(error);
        }

        try
        {
            return PathOf(file) is { } path ? new[] { path } : NotLocal();
        }
        finally
        {
            GLib.g_object_unref(file);
        }
    }

    private static Result<IReadOnlyList<string>, FileDialogError> Many(nint list, nint error)
    {
        if (list == 0)
        {
            return Failure(error);
        }

        try
        {
            var count = Gtk.g_list_model_get_n_items(list);
            var paths = new List<string>((int)count);
            for (var i = 0u; i < count; i++)
            {
                var file = Gtk.g_list_model_get_item(list, i);
                var path = PathOf(file);
                GLib.g_object_unref(file);
                if (path is null)
                {
                    return NotLocal();
                }

                paths.Add(path);
            }

            return paths;
        }
        finally
        {
            GLib.g_object_unref(list);
        }
    }

    private static string? PathOf(nint file) => GLib.TakeString(Gtk.g_file_get_path(file));

    private static FileDialogError NotLocal() => new FileDialogError.AccessDenied("a location that is not a local file");

    /// <summary>A dialog that ended without a file: dismissed or cancelled is a dismissal, anything else is why it failed.</summary>
    private static FileDialogError Failure(nint error)
    {
        if (error == 0)
        {
            return new FileDialogError.Dismissed();
        }

        var domain = (uint)Marshal.ReadInt32(error);
        var code = Marshal.ReadInt32(error, 4);
        var message = GLib.ErrorMessage(error);
        GLib.g_error_free(error);
        var dismissed = domain == Gtk.gtk_dialog_error_quark() && code is Gtk.DialogErrorCancelled or Gtk.DialogErrorDismissed;
        return dismissed ? new FileDialogError.Dismissed() : new FileDialogError.Unavailable($"The file dialog failed: {message}");
    }

    /// <summary>One dialog: its kind, its cancellable while it is open, and its answer.</summary>
    private sealed class Pending(FileDialogKind kind)
    {
        public FileDialogKind Kind { get; } = kind;

        public nint Cancellable { get; set; }

        public Action? Done { get; set; }

        public TaskCompletionSource<Result<IReadOnlyList<string>, FileDialogError>> Answer { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Cancels the dialog (loop thread); the answer, a dismissal, arrives on a later turn.</summary>
        public void Cancel()
        {
            if (Cancellable != 0)
            {
                GLib.g_cancellable_cancel(Cancellable);
            }
        }
    }
}
