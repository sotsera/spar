using System.Runtime.InteropServices;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// A native message owned by the window: GTK 4's <c>GtkAlertDialog</c>, shown without a nested main loop (the loop keeps
/// running, plan section 3), its answer delivered as a task on the loop. A dialog the host takes down itself (the
/// hung-page dialog when the page answers again, the shutdown prompt when the shutdown finishes) is cancelled through
/// its <c>GCancellable</c>.
/// </summary>
internal sealed class AlertDialog
{
    /// <summary>The answer of a dialog taken down by <see cref="Dismiss"/> before the user answered.</summary>
    public const int Dismissed = -1;

    private readonly TaskCompletionSource<int> _answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _cancel;
    private nint _cancellable;
    private bool _dismissed;

    private AlertDialog(int cancel) => _cancel = cancel;

    /// <summary>The index of the button chosen, the cancel button's when the dialog was closed, or <see cref="Dismissed"/>.</summary>
    public Task<int> Answer => _answered.Task;

    /// <summary>
    /// Shows <paramref name="message"/> with <paramref name="detail"/> over <paramref name="parent"/> (loop thread), with
    /// <paramref name="buttons"/>, the first the default and the last the cancel button. Completes with the index of the
    /// button chosen, or the cancel button's when the dialog was closed or its window went away.
    /// </summary>
    public static Task<int> ChooseAsync(nint parent, string message, string detail, params string[] buttons) =>
        Show(parent, message, detail, buttons, buttons.Length - 1).Answer;

    /// <summary>
    /// Shows the dialog (loop thread) over <paramref name="parent"/>, or over no window when it is 0, with the first of
    /// <paramref name="buttons"/> the default and <paramref name="cancelButton"/> the one Escape and closing choose.
    /// </summary>
    public static unsafe AlertDialog Show(nint parent, string message, string detail, string[] buttons, int cancelButton)
    {
        var shown = new AlertDialog(cancelButton);
        var dialog = GLib.g_object_new_with_properties(Gtk.gtk_alert_dialog_get_type(), 0, null, null);
        Gtk.gtk_alert_dialog_set_message(dialog, message);
        Gtk.gtk_alert_dialog_set_detail(dialog, detail);
        SetButtons(dialog, buttons);
        Gtk.gtk_alert_dialog_set_default_button(dialog, 0);
        Gtk.gtk_alert_dialog_set_cancel_button(dialog, cancelButton);

        shown._cancellable = GLib.g_cancellable_new();
        var state = GCHandle.Alloc(shown);
        Gtk.gtk_alert_dialog_choose(dialog, parent, shown._cancellable, &Chosen, GCHandle.ToIntPtr(state));
        return shown;
    }

    /// <summary>Takes the dialog down (loop thread): its answer becomes <see cref="Dismissed"/> unless the user answered first.</summary>
    public void Dismiss()
    {
        if (_cancellable == 0 || _answered.Task.IsCompleted)
        {
            return;
        }

        _dismissed = true;

        // The dialog destroys its window from the cancellable's signal, so this runs on the loop only.
        GLib.g_cancellable_cancel(_cancellable);
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

    /// <summary>The dialog's answer, on the loop: the dialog (owned since its creation) and its cancellable are released here.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void Chosen(nint dialog, nint result, nint data)
    {
        var state = GCHandle.FromIntPtr(data);
        var shown = (AlertDialog)state.Target!;
        state.Free();
        try
        {
            nint error = 0;
            var button = Gtk.gtk_alert_dialog_choose_finish(dialog, result, &error);
            if (error != 0)
            {
                GLib.g_error_free(error);
            }

            shown._answered.TrySetResult(shown._dismissed ? Dismissed : button < 0 ? shown._cancel : button);
        }
        catch (Exception exception)
        {
            shown._answered.TrySetException(exception);
        }
        finally
        {
            GLib.g_object_unref(dialog);
            GLib.g_object_unref(shown._cancellable);
            shown._cancellable = 0;
        }
    }
}
