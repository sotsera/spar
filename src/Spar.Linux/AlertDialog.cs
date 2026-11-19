using System.Runtime.InteropServices;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// A native message owned by the window: GTK 4's <c>GtkAlertDialog</c>, shown without a nested main loop (the loop keeps
/// running, plan section 3), its answer delivered as a task on the loop.
/// </summary>
internal static class AlertDialog
{
    /// <summary>
    /// Shows <paramref name="message"/> with <paramref name="detail"/> over <paramref name="parent"/> (loop thread), with
    /// <paramref name="buttons"/>, the first the default and the last the cancel button. Completes with the index of the
    /// button chosen, or the cancel button's when the dialog was closed or its window went away.
    /// </summary>
    public static unsafe Task<int> ChooseAsync(nint parent, string message, string detail, params string[] buttons)
    {
        var answered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = GLib.g_object_new_with_properties(Gtk.gtk_alert_dialog_get_type(), 0, null, null);
        Gtk.gtk_alert_dialog_set_message(dialog, message);
        Gtk.gtk_alert_dialog_set_detail(dialog, detail);
        SetButtons(dialog, buttons);
        Gtk.gtk_alert_dialog_set_default_button(dialog, 0);
        Gtk.gtk_alert_dialog_set_cancel_button(dialog, buttons.Length - 1);

        var state = GCHandle.Alloc(new Pending(answered, buttons.Length - 1));
        Gtk.gtk_alert_dialog_choose(dialog, parent, 0, &Chosen, GCHandle.ToIntPtr(state));
        return answered.Task;
    }

    private static unsafe void SetButtons(nint dialog, string[] buttons)
    {
        var labels = new nint[buttons.Length + 1];
        try
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                labels[i] = Marshal.StringToCoTaskMemUTF8(buttons[i]);
            }

            fixed (nint* array = labels)
            {
                // The dialog copies the labels.
                Gtk.gtk_alert_dialog_set_buttons(dialog, array);
            }
        }
        finally
        {
            foreach (var label in labels)
            {
                Marshal.FreeCoTaskMem(label);
            }
        }
    }

    /// <summary>The dialog's answer, on the loop: the dialog (owned since its creation) is released here.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void Chosen(nint dialog, nint result, nint data)
    {
        var state = GCHandle.FromIntPtr(data);
        var pending = (Pending)state.Target!;
        state.Free();
        try
        {
            nint error = 0;
            var button = Gtk.gtk_alert_dialog_choose_finish(dialog, result, &error);
            if (error != 0)
            {
                GLib.g_error_free(error);
            }

            pending.Answered.TrySetResult(button < 0 ? pending.Cancel : button);
        }
        catch (Exception exception)
        {
            pending.Answered.TrySetException(exception);
        }
        finally
        {
            GLib.g_object_unref(dialog);
        }
    }

    private sealed record Pending(TaskCompletionSource<int> Answered, int Cancel);
}
