using System.Runtime.InteropServices;
using System.Text;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The backend's clipboard on Linux (decision "M3c's decide-first questions", item 5): the display's
/// <c>GdkClipboard</c>, used on the main loop through its asynchronous calls, which never block it; another program's
/// slow answer only delays the read's completion. GTK owns the text it puts there until another program takes the
/// clipboard. Without a display the calls are <see cref="ClipboardError.Unavailable"/>.
/// </summary>
internal sealed class LinuxClipboard(Func<MainLoop?> loop) : IClipboard
{
    public Task<Result<ClipboardError>> SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) > IClipboard.MaxTextBytes)
        {
            return Task.FromResult<Result<ClipboardError>>(new ClipboardError.TooLarge(IClipboard.MaxTextBytes));
        }

        return OnLoop<Result<ClipboardError>>(done =>
        {
            if (Clipboard() is not { } clipboard)
            {
                done.TrySetResult(NoDisplay());
                return;
            }

            Gtk.gdk_clipboard_set_text(clipboard, text);
            done.TrySetResult(Result.Success);
        }, cancellationToken);
    }

    public Task<Result<string, ClipboardError>> GetTextAsync(CancellationToken cancellationToken) =>
        OnLoop<Result<string, ClipboardError>>(done =>
        {
            if (Clipboard() is not { } clipboard)
            {
                done.TrySetResult(NoDisplay());
                return;
            }

            ReadText(clipboard, done);
        }, cancellationToken);

    private static unsafe void ReadText(nint clipboard, TaskCompletionSource<Result<string, ClipboardError>> done) =>
        Gtk.gdk_clipboard_read_text_async(clipboard, 0, &TextRead, GCHandle.ToIntPtr(GCHandle.Alloc(done)));

    [UnmanagedCallersOnly]
    private static unsafe void TextRead(nint clipboard, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var done = (TaskCompletionSource<Result<string, ClipboardError>>)handle.Target!;
        handle.Free();
        try
        {
            nint error = 0;
            var text = Gtk.gdk_clipboard_read_text_finish(clipboard, result, &error);
            if (text != 0)
            {
                done.TrySetResult(GLib.TakeString(text) ?? "");
                return;
            }

            // No text on the clipboard (an image, a file, or nothing) reads as empty; a failure says why.
            var message = GLib.ErrorMessage(error);
            if (error != 0)
            {
                GLib.g_error_free(error);
            }

            done.TrySetResult(message.Length == 0 || message.Contains("No compatible", StringComparison.OrdinalIgnoreCase)
                ? ""
                : new ClipboardError.Unavailable($"The clipboard could not be read: {message}"));
        }
        catch (Exception exception)
        {
            done.TrySetException(exception);
        }
    }

    private static nint? Clipboard()
    {
        var display = Gtk.gdk_display_get_default();
        return display == 0 ? null : Gtk.gdk_display_get_clipboard(display);
    }

    private static ClipboardError NoDisplay() => new ClipboardError.Unavailable("There is no display, so no clipboard.");

    private Task<T> OnLoop<T>(Action<TaskCompletionSource<T>> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (loop() is not { } ui)
        {
            done.TrySetException(new InvalidOperationException("The app's main loop is not running."));
            return done.Task;
        }

        ui.Post(() =>
        {
            try
            {
                work(done);
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
            }
        }, "clipboard");
        return done.Task.WaitAsync(cancellationToken);
    }
}
