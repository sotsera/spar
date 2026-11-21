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
    private readonly WindowDialogs? _owner;
    private nint _cancellable;
    private bool _dismissed;

    private AlertDialog(int cancel, WindowDialogs? owner)
    {
        _cancel = cancel;
        _owner = owner;
    }

    /// <summary>The index of the button chosen, the cancel button's when the dialog was closed, or <see cref="Dismissed"/>.</summary>
    public Task<int> Answer => _answered.Task;

    /// <summary>
    /// Shows <paramref name="message"/> with <paramref name="detail"/> over <paramref name="parent"/> (loop thread), with
    /// <paramref name="buttons"/>, the first the default and the last the cancel button. Completes with the index of the
    /// button chosen, or the cancel button's when the dialog was closed or its window went away.
    /// </summary>
    public static Task<int> ChooseAsync(nint parent, WindowDialogs? owner, string message, string detail, params string[] buttons) =>
        Show(parent, owner, message, detail, buttons, buttons.Length - 1).Answer;

    /// <summary>
    /// Shows the dialog (loop thread) over <paramref name="parent"/>, or over no window when it is 0, with the first of
    /// <paramref name="buttons"/> the default and <paramref name="cancelButton"/> the one Escape and closing choose.
    /// A dialog over a window is one of its <paramref name="owner"/>'s until its answer, so the window is not destroyed
    /// before it (<see cref="WindowDialogs"/>).
    /// </summary>
    public static unsafe AlertDialog Show(nint parent, WindowDialogs? owner, string message, string detail, string[] buttons, int cancelButton)
    {
        var shown = new AlertDialog(cancelButton, owner);
        var dialog = GLib.g_object_new_with_properties(Gtk.gtk_alert_dialog_get_type(), 0, null, null);
        Gtk.gtk_alert_dialog_set_message(dialog, message);
        Gtk.gtk_alert_dialog_set_detail(dialog, detail);
        SetButtons(dialog, buttons);
        Gtk.gtk_alert_dialog_set_default_button(dialog, 0);
        Gtk.gtk_alert_dialog_set_cancel_button(dialog, cancelButton);

        shown._cancellable = GLib.g_cancellable_new();
        var state = GCHandle.Alloc(shown);
        Gtk.gtk_alert_dialog_choose(dialog, parent, shown._cancellable, &Chosen, GCHandle.ToIntPtr(state));
        owner?.Opened(shown);
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
            shown._owner?.Answered(shown);
        }
    }
}

/// <summary>
/// The dialogs open over one window (loop thread), so its teardown takes them down and waits for their answers before
/// it destroys the window. <c>GtkAlertDialog</c> makes its window destroyed with its parent, but keeps it, unreferenced,
/// to destroy again when the answer is read (<c>gtk_alert_dialog_choose_finish</c>), and GLib delivers the answer of a
/// cancelled dialog on the loop's next turn, never at once: a window destroyed between the two left the answer a freed
/// window to destroy (in the CI container: a Development probe's segfault in <c>gtk_window_destroy</c> under
/// <c>gtk_alert_dialog_choose_finish</c>, after "window destroyed"; a cancel and the parent's destroy in one turn fail
/// GTK's window check every time; pitfalls).
/// </summary>
internal sealed class WindowDialogs
{
    private readonly List<AlertDialog> _open = [];

    /// <summary>Takes every open dialog down and waits, at most <paramref name="milliseconds"/>, for their answers.</summary>
    public async Task<bool> CloseAllAsync(int milliseconds)
    {
        if (_open.Count == 0)
        {
            return true;
        }

        var open = _open.ToArray();
        foreach (var dialog in open)
        {
            dialog.Dismiss();
        }

        var all = Task.WhenAll(open.Select(dialog => dialog.Answer));
        return await Task.WhenAny(all, Task.Delay(milliseconds)) == all;
    }

    internal void Opened(AlertDialog dialog) => _open.Add(dialog);

    internal void Answered(AlertDialog dialog) => _open.Remove(dialog);
}
