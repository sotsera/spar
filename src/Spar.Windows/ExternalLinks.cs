using Spar.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// External addresses the user follows from the page, opened outside the app (plan section 3b; decision "Opening links
/// and files outside the app"): only <c>http</c>, <c>https</c>, <c>mailto</c> and <c>tel</c>, never with credentials in
/// the address, never longer than <see cref="MaxLength"/>, and only after a user's action, which the caller checks.
/// <c>ShellExecuteW</c> can take hundreds of milliseconds and loads shell extensions, so it runs on an STA thread of its
/// own, never on the window's UI thread.
/// </summary>
internal static class ExternalLinks
{
    /// <summary>The longest address handed to the system: longer ones are refused, not cut.</summary>
    public const int MaxLength = NavigationRules.MaxExternalLength;

    /// <summary>Why <paramref name="address"/> may not be opened outside the app; null when it may (the core's rule).</summary>
    public static string? Refusal(string address) => NavigationRules.ExternalRefusal(address, PageServer.Origin);

    /// <summary>Opens an allowed address with the system's handler, on a thread of its own; failures are logged.</summary>
    public static void Open(string address, HostLog log)
    {
        var thread = new Thread(() => Execute(address, log)) { IsBackground = true, Name = "Spar external link" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static unsafe void Execute(string address, HostLog log)
    {
        try
        {
            HINSTANCE result;
            fixed (char* verb = "open")
            fixed (char* file = address)
            {
                result = PInvoke.ShellExecute(HWND.Null, verb, file, null, null, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
            }

            // ShellExecute returns a value above 32 on success, an error code otherwise.
            if ((nint)result.Value <= 32)
            {
                log.Warn($"The system could not open the address (ShellExecute returned {(nint)result.Value}).");
            }
        }
        catch (Exception exception)
        {
            log.Error("Opening an address outside the app failed", exception);
        }
    }
}
