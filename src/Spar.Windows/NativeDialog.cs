using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// The host's native messages, owned by the app's window: the close confirmation of the two-stage close ("Close
/// anyway" / "Keep open", decision "Two-stage close") and the message before a failure exit. A task dialog needs the
/// Common Controls 6 manifest Spar.Sdk gives Windows apps; without it the message box stands in. Both are modal: they
/// run their own message loop on the UI thread, which keeps the window and the WebView pumping.
/// </summary>
internal static unsafe class NativeDialog
{
    private const int CloseAnywayId = 100;
    private const int KeepOpenId = 101;

    /// <summary>Asks <paramref name="question"/>; true when the user chose "Close anyway". Keep open is the default.</summary>
    public static bool ConfirmClose(HWND owner, string title, string question)
    {
        try
        {
            return TaskDialog(owner, title, question, closeOnly: false) == CloseAnywayId;
        }
        catch (EntryPointNotFoundException)
        {
            var text = question + "\n\nYes closes anyway; No keeps the window open.";
            var answer = PInvoke.MessageBox(owner, text, title, MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONWARNING | MESSAGEBOX_STYLE.MB_DEFBUTTON2);
            return answer == MESSAGEBOX_RESULT.IDYES;
        }
    }

    /// <summary>Shows an error before the app exits.</summary>
    public static void ShowError(HWND owner, string title, string message)
    {
        try
        {
            _ = TaskDialog(owner, title, message, closeOnly: true);
        }
        catch (EntryPointNotFoundException)
        {
            _ = PInvoke.MessageBox(owner, message, title, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
        }
    }

    private static int TaskDialog(HWND owner, string title, string text, bool closeOnly)
    {
        fixed (char* titleText = title)
        fixed (char* content = text)
        fixed (char* closeText = "Close anyway")
        fixed (char* keepText = "Keep open")
        {
            var buttons = stackalloc TASKDIALOG_BUTTON[2];
            buttons[0] = new TASKDIALOG_BUTTON { nButtonID = CloseAnywayId, pszButtonText = closeText };
            buttons[1] = new TASKDIALOG_BUTTON { nButtonID = KeepOpenId, pszButtonText = keepText };

            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                hwndParent = owner,
                dwFlags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION | TASKDIALOG_FLAGS.TDF_POSITION_RELATIVE_TO_WINDOW,
                pszWindowTitle = titleText,
                pszContent = content,
            };

            if (closeOnly)
            {
                config.dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CLOSE_BUTTON;
            }
            else
            {
                config.cButtons = 2;
                config.pButtons = buttons;
                config.nDefaultButton = KeepOpenId;
            }

            var result = PInvoke.TaskDialogIndirect(config, out var pressed, out _, out _);
            return result.Succeeded ? pressed : KeepOpenId;
        }
    }
}
