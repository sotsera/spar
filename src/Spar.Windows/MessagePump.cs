using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// A message loop inside a window procedure, for a message that must not return before work that needs the thread is
/// done (<c>WM_ENDSESSION</c>: Windows may end the process once it returns). It keeps the UI thread's posted work and
/// every window's messages flowing, as a modal dialog's loop does.
/// </summary>
internal static class MessagePump
{
    /// <summary>The longest single wait for a message, so <c>done</c> is checked again soon after work elsewhere ends.</summary>
    private const uint WaitSliceMilliseconds = 20;

    /// <summary>
    /// Pumps the calling thread's messages until <paramref name="done"/> is true or the <see cref="Stopwatch"/>
    /// timestamp <paramref name="limit"/> has passed. Returns the exit code of a <c>WM_QUIT</c> it took, which the caller
    /// posts again once it returns, or null.
    /// </summary>
    public static unsafe int? Until(Func<bool> done, long limit)
    {
        int? quit = null;
        while (!done())
        {
            var remaining = (limit - Stopwatch.GetTimestamp()) * 1_000 / Stopwatch.Frequency;
            if (remaining <= 0)
            {
                break;
            }

            while (PInvoke.PeekMessage(out var message, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (message.message == PInvoke.WM_QUIT)
                {
                    quit = (int)message.wParam.Value;
                    continue;
                }

                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
                if (done())
                {
                    return quit;
                }
            }

            var wait = (uint)Math.Min(remaining, WaitSliceMilliseconds);
            PInvoke.MsgWaitForMultipleObjectsEx(
                0, (HANDLE*)null, wait, QUEUE_STATUS_FLAGS.QS_ALLINPUT, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
        }

        return quit;
    }
}
