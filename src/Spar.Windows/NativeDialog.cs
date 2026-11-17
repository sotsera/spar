using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Spar.Hosting;
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
    private const int DownloadId = 102;

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

    /// <summary>
    /// The start-up message (plan section 3, "Windows dialog"): "&lt;app&gt; can't start", what is wrong and what to do,
    /// a link and a button to the component's page when it has one, and expandable details with the problem line and
    /// the log file. The dialog never opens links itself: the host does, through <c>ShellExecuteW</c>, and only the
    /// problem's own address. Without Common Controls 6 a message box asks whether to open that page.
    /// </summary>
    public static void ShowStartupProblem(HWND owner, string title, StartupProblem problem, string? logFile)
    {
        var instruction = $"{title} can't start";
        try
        {
            StartupTaskDialog(owner, title, instruction, problem, logFile);
        }
        catch (EntryPointNotFoundException)
        {
            var text = problem.HelpUrl is null ? problem.Details : problem.Details + "\n\nOpen Microsoft's download page now?";
            var buttons = problem.HelpUrl is null ? MESSAGEBOX_STYLE.MB_OK : MESSAGEBOX_STYLE.MB_YESNO;
            var answer = PInvoke.MessageBox(owner, instruction + "\n\n" + text, title, buttons | MESSAGEBOX_STYLE.MB_ICONERROR | MESSAGEBOX_STYLE.MB_SETFOREGROUND);
            if (answer == MESSAGEBOX_RESULT.IDYES && problem.HelpUrl is not null)
            {
                Open(owner, problem.HelpUrl);
            }
        }
    }

    private static void StartupTaskDialog(HWND owner, string title, string instruction, StartupProblem problem, string? logFile)
    {
        var content = problem.Details;
        if (problem.HelpUrl is not null)
        {
            content += $"\n\n<a href=\"{problem.HelpUrl}\">Get the {problem.Component} from Microsoft</a>";
        }

        var details = StartupReport.Describe(problem) + (logFile is null ? "" : $"\nLog: {logFile}");
        var link = GCHandle.Alloc(problem.HelpUrl);
        try
        {
            fixed (char* titleText = title)
            fixed (char* instructionText = instruction)
            fixed (char* contentText = content)
            fixed (char* detailsText = details)
            fixed (char* downloadText = $"Download the {problem.Component}")
            {
                var button = new TASKDIALOG_BUTTON { nButtonID = DownloadId, pszButtonText = downloadText };
                var config = new TASKDIALOGCONFIG
                {
                    cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                    hwndParent = owner,
                    dwFlags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION | TASKDIALOG_FLAGS.TDF_ENABLE_HYPERLINKS
                        | (owner.IsNull ? 0 : TASKDIALOG_FLAGS.TDF_POSITION_RELATIVE_TO_WINDOW),
                    dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CLOSE_BUTTON,
                    pszWindowTitle = titleText,
                    pszMainInstruction = instructionText,
                    pszContent = contentText,
                    pszExpandedInformation = detailsText,
                    pfCallback = &StartupCallback,
                    lpCallbackData = GCHandle.ToIntPtr(link),
                };
                config.Anonymous1.pszMainIcon = PInvoke.TD_ERROR_ICON;
                if (problem.HelpUrl is not null)
                {
                    config.cButtons = 1;
                    config.pButtons = &button;
                }

                _ = PInvoke.TaskDialogIndirect(config, out _, out _, out _);
            }
        }
        finally
        {
            link.Free();
        }
    }

    /// <summary>The start-up dialog's notifications: a click on the link or on the download button opens the page.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT StartupCallback(HWND window, uint notification, WPARAM wParam, LPARAM lParam, nint data)
    {
        try
        {
            var opensPage = notification == (uint)TASKDIALOG_NOTIFICATIONS.TDN_HYPERLINK_CLICKED
                || (notification == (uint)TASKDIALOG_NOTIFICATIONS.TDN_BUTTON_CLICKED && (int)wParam.Value == DownloadId);
            if (opensPage && GCHandle.FromIntPtr(data).Target is string url)
            {
                Open(window, url);

                // S_FALSE keeps the dialog open after the download button, so the user can read it again.
                return notification == (uint)TASKDIALOG_NOTIFICATIONS.TDN_BUTTON_CLICKED ? HRESULT.S_FALSE : HRESULT.S_OK;
            }
        }
        catch (Exception)
        {
            // Nothing may unwind into the dialog; a page that does not open leaves the dialog as it is.
        }

        return HRESULT.S_OK;
    }

    /// <summary>Opens an address with the user's browser.</summary>
    private static void Open(HWND owner, string url)
    {
        fixed (char* verb = "open")
        fixed (char* file = url)
        {
            _ = PInvoke.ShellExecute(owner, verb, file, null, null, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
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
