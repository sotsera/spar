using System.Runtime.InteropServices;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// Files dropped from the OS (decision "The drop design, from the file-drop spike", item 3). WebKitGTK gives the page
/// no <c>File</c>s for them (WebKit bugs 320301 and 323277), and a drop target of the host's own would take the drop
/// from WebKit and leave its drag state stuck, so the host only watches: a <c>GtkDropControllerMotion</c> on the view
/// keeps each drag's drop while WebKit keeps the drag and the page's events; the bridge relay's world reports the
/// page's first trusted <c>dragenter</c>, when the host reads the drag's file list, and each trusted <c>drop</c>, on a
/// handler of its own, which neither the page's world nor a frame can reach. The window's <c>DropInbox</c> pairs the
/// files with the drop, so only a drop the user made becomes grants.
/// </summary>
internal sealed partial class BrowserView
{
    /// <summary>The relay's handler for trusted drops, in the bridge's world only.</summary>
    public const string DropHandler = "sparDrop";

    /// <summary>How long after the page's first trusted <c>dragenter</c> the host reads the drag's files.</summary>
    private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(150);

    private nuint _dropMessageHandler;
    private nuint _dragEnterHandler;
    private nint _dragMotion;
    private nint _enteredDrop;
    private nint _lastDrop;

    /// <summary>The relay's drop handler, on the content manager, before the view loads anything (loop thread).</summary>
    private unsafe void ListenForDrops(nint self)
    {
        if (WebKit.webkit_user_content_manager_register_script_message_handler(_content, DropHandler, ScriptBridge.World) == 0)
        {
            throw new InvalidOperationException("WebKit refused the drop's script message handler.");
        }

        _dropMessageHandler = GLib.g_signal_connect_data(
            _content, "script-message-received::" + DropHandler, (delegate* unmanaged<nint, nint, nint, void>)&OnDropMessage, self, 0, 0);
    }

    /// <summary>The drag watcher on the view (loop thread); the view owns it.</summary>
    private unsafe void WatchDrags(nint self)
    {
        _dragMotion = Gtk.gtk_drop_controller_motion_new();
        _dragEnterHandler = GLib.g_signal_connect_data(
            _dragMotion, "enter", (delegate* unmanaged<nint, double, double, nint, void>)&OnDragEnter, self, 0, 0);
        Gtk.gtk_widget_add_controller(_view, _dragMotion);
    }

    private void StopWatchingDrops()
    {
        if (_dragMotion != 0)
        {
            GLib.g_signal_handler_disconnect(_dragMotion, _dragEnterHandler);
            _dragMotion = 0;
        }

        if (_enteredDrop != 0)
        {
            GLib.g_object_unref(_enteredDrop);
            _enteredDrop = 0;
        }

        if (_content != 0)
        {
            GLib.g_signal_handler_disconnect(_content, _dropMessageHandler);
            WebKit.webkit_user_content_manager_unregister_script_message_handler(_content, DropHandler, ScriptBridge.World);
        }
    }

    /// <summary>
    /// A drag entered the view: its drop is kept, and read only when the relay reports the page's first trusted
    /// <c>dragenter</c>. WebKit reads the drag's data as it enters; a read of the host's own at the same moment made
    /// WebKit's drag hang, unanswered, one drag in three in Spar's CI image, and every drag after it in that process.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void OnDragEnter(nint controller, double x, double y, nint data) => Dispatch(data, "drag entered", browser =>
    {
        var drop = Gtk.gtk_drop_controller_motion_get_drop(controller);
        if (drop == 0 || drop == browser._enteredDrop)
        {
            return;
        }

        if (browser._enteredDrop != 0)
        {
            GLib.g_object_unref(browser._enteredDrop);
        }

        browser._enteredDrop = GLib.g_object_ref(drop);
    });

    /// <summary>The page saw the drag enter, so WebKit has its data: the host reads the drag's files now, once.</summary>
    private unsafe void ReadEnteredDrop()
    {
        var drop = _enteredDrop;
        if (drop == 0 || drop == _lastDrop)
        {
            return;
        }

        _lastDrop = drop;
        var read = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        GLib.g_object_ref(drop);
        Gtk.gdk_drop_read_value_async(drop, Gtk.gdk_file_list_get_type(), GLib.PriorityDefault, 0, &OnFilesRead, GCHandle.ToIntPtr(GCHandle.Alloc(read)));
        _bridge.Window?.Drops.DragEntered(read.Task);
    }

    /// <summary>The drag's files, on the loop; a drag without any (text, or an element of the page) fails the read.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnFilesRead(nint drop, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var read = (TaskCompletionSource<IReadOnlyList<string>>)handle.Target!;
        handle.Free();
        try
        {
            nint error = 0;
            var value = Gtk.gdk_drop_read_value_finish(drop, result, &error);
            if (value == 0)
            {
                var message = GLib.ErrorMessage(error);
                GLib.g_error_free(error);
                read.TrySetException(new IOException($"The drag's files could not be read: {message}"));
                return;
            }

            read.TrySetResult(Paths(Gtk.g_value_get_boxed(value)));
        }
        catch (Exception exception)
        {
            read.TrySetException(exception);
        }
        finally
        {
            GLib.g_object_unref(drop);
        }
    }

    private static List<string> Paths(nint fileList)
    {
        var paths = new List<string>();
        var list = Gtk.gdk_file_list_get_files(fileList);
        try
        {
            for (var node = list; node != 0; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                if (GLib.TakeString(Gtk.g_file_get_path(Marshal.ReadIntPtr(node))) is { } path)
                {
                    paths.Add(path);
                }
            }
        }
        finally
        {
            Gtk.g_slist_free(list);
        }

        return paths;
    }

    /// <summary>
    /// The relay saw a trusted drag of files enter the page (<c>enter</c>), or drop on it (<c>drop</c>): the drag's files
    /// are read, or the drag that entered last becomes a drop the page may claim.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void OnDropMessage(nint manager, nint value, nint data) => Dispatch(data, "trusted drag", browser =>
    {
        switch (GLib.TakeString(WebKit.jsc_value_to_string(value)))
        {
            case "enter":
                // A moment after the page saw it, so WebKit is done with its own read of the drag's data: in Spar's CI image,
                // a read at the page's dragenter still hung WebKit's drag once in 15 drags, and none in 21 at 150 ms.
                _ = Task.Delay(ReadDelay).ContinueWith(_ => browser._ui.Post(browser.ReadEnteredDrop, "drag read"), TaskScheduler.Default);
                break;
            case "drop":
                browser._bridge.Window?.Drops.Dropped();
                break;
        }
    });
}
