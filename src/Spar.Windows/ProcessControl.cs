using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Spar.Windows;

/// <summary>
/// The two places where a process is ended, each only after the user chose it (plan section 3, "Hung pages"; the
/// shutdown hang policy): a hung page's renderer for "Reload page" or "Close anyway", and this process for "Exit now".
/// Spar never ends a process on its own.
/// </summary>
internal static class ProcessControl
{
    /// <summary>The exit code a renderer ended by Spar gets.</summary>
    private const uint RendererExitCode = 1;

    /// <summary>Ends a renderer process of the app's own browser; false when it could not be opened or ended.</summary>
    public static bool Terminate(int processId)
    {
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, (uint)processId);
        if (process.IsNull)
        {
            return false;
        }

        try
        {
            return PInvoke.TerminateProcess(process, RendererExitCode);
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    /// <summary>
    /// Ends this process at once with <paramref name="exitCode"/>, skipping every cleanup still running or waiting:
    /// the user chose "Exit now" because that cleanup did not finish, so nothing that could wait on it may run first.
    /// </summary>
    public static void ExitNow(int exitCode) => PInvoke.TerminateProcess(PInvoke.GetCurrentProcess(), (uint)exitCode);
}
