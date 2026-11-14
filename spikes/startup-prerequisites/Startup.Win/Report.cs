using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Startup.Win;

// Shows a start-up problem: stderr (inherited handle, else the parent's console), then one native dialog, then
// optionally the Application event log.
internal static unsafe class Report
{
    private const int DownloadButton = 100;
    private static Log? s_log;
    private static string? s_link;
    private static nint s_hook;

    public static void Show(StartupProblem p, Options o, Log log)
    {
        s_log = log;
        s_link = p.Link;
        var text = $"{o.AppName} cannot start: {p.Summary}\n{p.Fix}" + (p.Link is null ? "" : $"\nDownload: {p.Link}") +
            $"\nDetails: {p.Details}\nExit code {p.ExitCode}.\n";
        if (o.Stderr != "off")
        {
            log.Mark("stderr", ("path", WriteStderr(text)));
        }

        if (o.EventLog)
        {
            log.Mark("eventlog", ("ok", WriteEventLog(o.AppName, text)), ("error", Marshal.GetLastPInvokeError()));
        }

        switch (o.Ui)
        {
            case "none":
                return;
            case "messagebox":
                ShowMessageBox(p, o);
                return;
            default:
                try
                {
                    var hr = ShowTaskDialog(p, o);
                    if (hr < 0)
                    {
                        log.Mark("taskdialog-failed", ("hr", Log.Hr(hr)));
                        ShowMessageBox(p, o);
                    }
                }
                catch (EntryPointNotFoundException e)
                {
                    // comctl32 v5 (no Common Controls 6 manifest) has no TaskDialogIndirect.
                    log.Mark("taskdialog-unavailable", ("error", e.Message));
                    if (o.Ui == "auto")
                    {
                        ShowMessageBox(p, o);
                    }
                }

                return;
        }
    }

    // A GUI-subsystem process has valid standard handles only when its parent passed them (a pipe or file
    // redirection). Otherwise it attaches to the parent's console, if there is one, and writes there.
    private static string WriteStderr(string text)
    {
        var handle = Native.GetStdHandle(Native.StdErrorHandle);
        var type = handle is 0 or -1 ? Native.FileTypeUnknown : Native.GetFileType(handle);
        var path = type switch { Native.FileTypePipe => "inherited-pipe", Native.FileTypeDisk => "inherited-file", Native.FileTypeChar => "inherited-console", _ => "" };
        var owned = false;
        if (path.Length == 0)
        {
            if (!Native.AttachConsole(Native.AttachParentProcess))
            {
                return $"none (AttachConsole error {Marshal.GetLastPInvokeError()})";
            }

            // GENERIC_READ | GENERIC_WRITE, FILE_SHARE_WRITE, OPEN_EXISTING
            handle = Native.CreateFile("CONOUT$", 0xC0000000, 0x2, 0, 3, 0, 0);
            if (handle is 0 or -1)
            {
                return $"attached, CONOUT$ failed ({Marshal.GetLastPInvokeError()})";
            }

            owned = true;
            type = Native.FileTypeChar;
            path = "attached-console";
            text = "\n" + text; // the shell has already printed its prompt: start on a new line
        }

        bool ok;
        int written;
        if (type == Native.FileTypeChar)
        {
            var console = text.Replace("\n", "\r\n", StringComparison.Ordinal);
            fixed (char* c = console)
            {
                ok = Native.WriteConsoleW(handle, c, console.Length, out written, 0);
            }
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* b = bytes)
            {
                ok = Native.WriteFile(handle, b, bytes.Length, out written, 0);
            }
        }

        if (owned)
        {
            Native.CloseHandle(handle);
        }

        return $"{path} ok={ok} written={written}";
    }

    private static bool WriteEventLog(string source, string text)
    {
        var log = Native.RegisterEventSource(null, source);
        if (log == 0)
        {
            return false;
        }

        var s = Marshal.StringToHGlobalUni(text);
        var strings = stackalloc char*[1];
        strings[0] = (char*)s;
        var ok = Native.ReportEvent(log, 0x0001, 0, 1000, 0, 1, 0, strings, 0); // EVENTLOG_ERROR_TYPE
        Marshal.FreeHGlobal(s);
        Native.DeregisterEventSource(log);
        return ok;
    }

    private static int ShowTaskDialog(StartupProblem p, Options o)
    {
        var strings = new List<nint>();
        char* S(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var ptr = Marshal.StringToHGlobalUni(value);
            strings.Add(ptr);
            return (char*)ptr;
        }

        try
        {
            var content = p.Summary + "\n\n" + p.Fix;
            if (p.Link is not null)
            {
                content += $"\n\n<a href=\"{p.Link}\">Get the WebView2 Runtime from Microsoft</a>";
            }

            var button = new Native.TaskDialogButton { Id = DownloadButton, Text = S("&Download the WebView2 Runtime") };
            var config = new Native.TaskDialogConfig
            {
                Size = (uint)sizeof(Native.TaskDialogConfig),
                Flags = Native.TdfAllowDialogCancellation | (p.Link is null ? 0 : Native.TdfEnableHyperlinks),
                CommonButtons = Native.TdcbfClose,
                WindowTitle = S(o.AppName),
                MainIcon = Native.TdErrorIcon,
                MainInstruction = S($"{o.AppName} can't start"),
                Content = S(content),
                ButtonCount = p.Link is null ? 0u : 1u,
                Buttons = &button,
                DefaultButton = p.Link is null ? Native.IdClose : DownloadButton,
                ExpandedInformation = S($"{p.Details}\nExit code: {p.ExitCode}"),
                CollapsedControlText = S("Show details"),
                ExpandedControlText = S("Hide details"),
                Callback = &TaskDialogCallback,
            };
            s_log!.Mark("taskdialog-call", ("configSize", sizeof(Native.TaskDialogConfig)));
            int pressed;
            var hr = Native.TaskDialogIndirect(&config, &pressed, null, null);
            s_log.Mark("taskdialog-closed", ("hr", Log.Hr(hr)), ("button", pressed));
            return hr;
        }
        finally
        {
            strings.ForEach(Marshal.FreeHGlobal);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int TaskDialogCallback(nint hwnd, uint msg, nint wParam, nint lParam, nint data)
    {
        try
        {
            switch (msg)
            {
                case Native.TdnCreated:
                    MarkWindow("taskdialog-created", hwnd);
                    break;
                case Native.TdnHyperlinkClicked:
                    // The task dialog never opens links itself.
                    OpenLink(hwnd, Marshal.PtrToStringUni(lParam) ?? "", "hyperlink");
                    break;
                case Native.TdnButtonClicked when wParam == DownloadButton:
                    OpenLink(hwnd, s_link!, "button");
                    return 1; // S_FALSE: keep the dialog open, so the user still sees what to do
                case Native.TdnDestroyed:
                    s_log?.Mark("taskdialog-destroyed");
                    break;
            }
        }
        catch (Exception e)
        {
            s_log?.Mark("taskdialog-callback-error", ("error", e.Message));
        }

        return 0;
    }

    private static void OpenLink(nint hwnd, string url, string from)
    {
        // Only the app's own constant link is opened, never markup text from elsewhere.
        if (url != s_link)
        {
            s_log?.Mark("link-refused", ("url", url));
            return;
        }

        var result = Native.ShellExecute(hwnd, "open", url, null, null, 1);
        s_log?.Mark("link-opened", ("from", from), ("url", url), ("result", (long)result));
    }

    private static void ShowMessageBox(StartupProblem p, Options o)
    {
        // A thread-local CBT hook reports when the box activates (its creation time and DPI).
        s_hook = Native.SetWindowsHookEx(Native.WhCbt, &CbtHook, 0, Native.GetCurrentThreadId());
        var text = $"{p.Summary}\n\n{p.Fix}" + (p.Link is null ? "" : "\n\nOpen Microsoft's download page now?") +
            $"\n\nDetails: {p.Details} (exit code {p.ExitCode})";
        var type = Native.MbIconError | Native.MbSetForeground | (p.Link is null ? Native.MbOk : Native.MbYesNo);
        s_log!.Mark("messagebox-call");
        var answer = Native.MessageBox(0, text, $"{o.AppName} can't start", type);
        s_log.Mark("messagebox-closed", ("answer", answer));
        Native.UnhookWindowsHookEx(s_hook);
        if (answer == Native.IdYes && p.Link is not null)
        {
            OpenLink(0, p.Link, "messagebox");
        }
    }

    [UnmanagedCallersOnly]
    private static nint CbtHook(int code, nint wParam, nint lParam)
    {
        if (code == Native.HcbtActivate)
        {
            MarkWindow("messagebox-activated", wParam);
        }

        return Native.CallNextHookEx(s_hook, code, wParam, lParam);
    }

    private static void MarkWindow(string evt, nint hwnd)
    {
        var cls = stackalloc char[64];
        var n = Native.GetClassName(hwnd, cls, 64);
        Native.GetWindowRect(hwnd, out var r);
        var awareness = Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(hwnd));
        s_log?.Mark(evt, ("hwnd", (long)hwnd), ("class", new string(cls, 0, n)), ("rect", r.ToString()),
            ("dpi", Native.GetDpiForWindow(hwnd)), ("awareness", awareness));
    }
}
