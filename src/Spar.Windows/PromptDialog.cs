using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>One button of a <see cref="PromptDialog"/>.</summary>
internal readonly record struct PromptButton(int Id, string Text);

/// <summary>
/// A two-button task dialog that the host can take down itself: the hung-page dialog ("Wait / Reload page", "Wait /
/// Close anyway") closes when the page answers again, and the shutdown prompt ("Keep waiting / Exit now") when the
/// shutdown finishes. It runs on an STA thread of its own, so the thread that shows it never waits: the window's UI
/// thread keeps pumping, and a shutdown stuck on another thread cannot stop the prompt. The first button is the default,
/// and Esc or the dialog's close button choose it, so dismissing the dialog never reloads or exits.
/// </summary>
internal sealed class PromptDialog
{
    /// <summary>The result when the host took the dialog down before the user answered.</summary>
    public const int Dismissed = -1;

    /// <summary><c>TDM_CLICK_BUTTON</c> (<c>WM_USER + 102</c>): presses a button of the dialog, here Cancel.</summary>
    private const uint ClickButton = 0x0400 + 102;

    /// <summary><c>IDCANCEL</c>: what Esc, the close button and <see cref="Dismiss"/> press.</summary>
    private const int Cancel = 2;

    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<int> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HWND _dialog;
    private bool _dismissed;
    private bool _takeForeground;

    private PromptDialog()
    {
    }

    /// <summary>The id of the button the user chose, or <see cref="Dismissed"/>.</summary>
    public Task<int> Result => _result.Task;

    /// <summary>
    /// Shows the dialog and returns at once. <paramref name="owner"/> is the window it belongs to, whose thread must keep
    /// pumping (the owner is disabled through messages to it), or <see cref="HWND.Null"/> for none.
    /// <paramref name="takeForeground"/> brings an unowned dialog to the front.
    /// </summary>
    public static PromptDialog Show(
        HWND owner,
        string title,
        string instruction,
        string content,
        PromptButton first,
        PromptButton second,
        bool takeForeground,
        Action<Exception> report)
    {
        var prompt = new PromptDialog { _takeForeground = takeForeground };
        var thread = new Thread(() => prompt.Run(owner, title, instruction, content, first, second, report))
        {
            Name = "Spar prompt",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return prompt;
    }

    /// <summary>Takes the dialog down; its result becomes <see cref="Dismissed"/> unless the user answered first. Any thread.</summary>
    public void Dismiss()
    {
        lock (_gate)
        {
            _dismissed = true;
            if (!_dialog.IsNull)
            {
                PInvoke.PostMessage(_dialog, ClickButton, new WPARAM(Cancel), default);
            }
        }
    }

    private void Run(HWND owner, string title, string instruction, string content, PromptButton first, PromptButton second, Action<Exception> report)
    {
        var pressed = first.Id;
        try
        {
            pressed = Show(owner, title, instruction, content, first, second);
        }
        catch (EntryPointNotFoundException)
        {
            // Without Common Controls 6 the message box stands in; Spar cannot take it down.
            var text = $"{instruction}\n\n{content}\n\nYes: {first.Text}. No: {second.Text}.";
            var answer = PInvoke.MessageBox(owner, text, title, MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONWARNING | MESSAGEBOX_STYLE.MB_SETFOREGROUND);
            pressed = answer == MESSAGEBOX_RESULT.IDNO ? second.Id : first.Id;
        }
        catch (Exception exception)
        {
            report(exception);
        }
        finally
        {
            bool dismissed;
            lock (_gate)
            {
                dismissed = _dismissed;
                _dialog = HWND.Null;
            }

            _result.TrySetResult(dismissed ? Dismissed : pressed == Cancel ? first.Id : pressed);
        }
    }

    private unsafe int Show(HWND owner, string title, string instruction, string content, PromptButton first, PromptButton second)
    {
        var self = GCHandle.Alloc(this);
        try
        {
            fixed (char* titleText = title)
            fixed (char* instructionText = instruction)
            fixed (char* contentText = content)
            fixed (char* firstText = first.Text)
            fixed (char* secondText = second.Text)
            {
                var buttons = stackalloc TASKDIALOG_BUTTON[2];
                buttons[0] = new TASKDIALOG_BUTTON { nButtonID = first.Id, pszButtonText = firstText };
                buttons[1] = new TASKDIALOG_BUTTON { nButtonID = second.Id, pszButtonText = secondText };
                var config = new TASKDIALOGCONFIG
                {
                    cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                    hwndParent = owner,
                    dwFlags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION
                        | (owner.IsNull ? 0 : TASKDIALOG_FLAGS.TDF_POSITION_RELATIVE_TO_WINDOW),
                    pszWindowTitle = titleText,
                    pszMainInstruction = instructionText,
                    pszContent = contentText,
                    cButtons = 2,
                    pButtons = buttons,
                    nDefaultButton = first.Id,
                    pfCallback = &Callback,
                    lpCallbackData = GCHandle.ToIntPtr(self),
                };
                config.Anonymous1.pszMainIcon = PInvoke.TD_WARNING_ICON;

                var result = PInvoke.TaskDialogIndirect(config, out var pressed, out _, out _);
                return result.Succeeded ? pressed : first.Id;
            }
        }
        finally
        {
            self.Free();
        }
    }

    /// <summary>On the dialog's thread: it now exists, so a dismissal asked for meanwhile takes it down at once.</summary>
    private void Created(HWND dialog)
    {
        lock (_gate)
        {
            _dialog = dialog;
            if (_dismissed)
            {
                PInvoke.PostMessage(dialog, ClickButton, new WPARAM(Cancel), default);
            }
        }

        if (_takeForeground)
        {
            _ = PInvoke.SetForegroundWindow(dialog);
        }
    }

    private void Destroyed()
    {
        lock (_gate)
        {
            _dialog = HWND.Null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT Callback(HWND window, uint notification, WPARAM wParam, LPARAM lParam, nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is not PromptDialog prompt)
            {
                return HRESULT.S_OK;
            }

            if (notification == (uint)TASKDIALOG_NOTIFICATIONS.TDN_CREATED)
            {
                prompt.Created(window);
            }
            else if (notification == (uint)TASKDIALOG_NOTIFICATIONS.TDN_DESTROYED)
            {
                prompt.Destroyed();
            }
        }
        catch (Exception)
        {
            // Nothing may unwind into the dialog.
        }

        return HRESULT.S_OK;
    }
}
