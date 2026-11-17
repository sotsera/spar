using Microsoft.Win32.SafeHandles;
using Spar.Hosting;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Console;

namespace Spar.Windows;

/// <summary>
/// How the Windows host reports a <see cref="StartupProblem"/> (plan section 3, "Missing prerequisites"): the line on
/// standard error (or the console the app was started from) and in the per-user log, always; then, unless the run is
/// automated, the app's hook (<c>spar.OnStartupProblem</c>), and Spar's native message when the hook showed none. The
/// hook and the message run on an STA thread of their own, so a window's UI thread keeps pumping meanwhile. The exit
/// code is the problem's, whatever happens.
/// </summary>
internal static class StartupFailure
{
    /// <summary><c>FILE_SHARE_WRITE</c>.</summary>
    private const uint FileShareWrite = 0x2;

    /// <summary>Reports a problem found before any window exists, on the process's main thread, and returns the exit code.</summary>
    public static int Report(StartupProblem problem, StartupContext context) =>
        ReportAsync(problem, context, HWND.Null).GetAwaiter().GetResult();

    /// <summary>
    /// Reports a problem; the message is owned by <paramref name="owner"/> when the app's window exists. Completes
    /// with the exit code once the message is closed.
    /// </summary>
    public static Task<int> ReportAsync(StartupProblem problem, StartupContext context, HWND owner)
    {
        var line = StartupReport.Describe(problem);
        context.Log.Error(line);
        WriteToParentConsole(line);

        if (context.Automated)
        {
            context.Log.Info("The automated mode shows no start-up message.");
            return Task.FromResult(problem.ExitCode);
        }

        var shown = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (StartupReport.AppShowedItsOwnMessage(context.Hook, problem, (message, exception) => context.Log.Error(message, exception)))
                {
                    context.Log.Info("The app showed its own start-up message.");
                }
                else
                {
                    NativeDialog.ShowStartupProblem(owner, context.Title, problem, context.Log.FilePath);
                    context.Log.Info("The start-up message was closed.");
                }
            }
            catch (Exception exception)
            {
                context.Log.Error("The start-up message could not be shown", exception);
            }
            finally
            {
                shown.TrySetResult(problem.ExitCode);
            }
        })
        {
            Name = "Spar start-up message",
            IsBackground = false,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return shown.Task;
    }

    /// <summary>
    /// A desktop executable started from a console window, without redirection, has no standard error: the line goes
    /// to the console it was started from, as the spike's batch file saw it (plan section 3, "Both OSes").
    /// </summary>
    private static unsafe void WriteToParentConsole(string line)
    {
        var standardError = PInvoke.GetStdHandle(STD_HANDLE.STD_ERROR_HANDLE);
        if ((nint)standardError.Value is not (0 or -1))
        {
            return;
        }

        if (!PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS))
        {
            return;
        }

        try
        {
            using SafeFileHandle console = Native.CreateFile("CONOUT$", Native.GenericWrite, FileShareWrite, 0, Native.OpenExisting, 0, 0);
            if (console.IsInvalid)
            {
                return;
            }

            var text = Environment.NewLine + line + Environment.NewLine;
            fixed (char* characters = text)
            {
                uint written;
                PInvoke.WriteConsole((HANDLE)console.DangerousGetHandle(), characters, (uint)text.Length, &written, null);
            }
        }
        finally
        {
            PInvoke.FreeConsole();
        }
    }
}

/// <summary>What a start-up report needs from the run: the app's title, its hook, the log and the automated mode.</summary>
internal sealed record StartupContext(string Title, Func<StartupProblem, bool>? Hook, HostLog Log, bool Automated);
