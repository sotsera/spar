using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Spar.Hosting;

namespace Spar.HotReload;

/// <summary>
/// The parent's console for a Windows app built as <c>WinExe</c> (decision "Step 3's two follow-ups"): such an app has
/// no console of its own, so the Ctrl+C that <c>dotnet watch</c> sends to stop it before a restart (to <c>dotnet run</c>'s
/// console) never reached it, and <c>dotnet watch</c> killed its whole tree 5 s later, Vite included. In a Development
/// build, when the app has no console and its parent has one, the app attaches to that console and answers its Ctrl+C
/// (and Ctrl+Break) with a normal close: the window's close request, guards included, or the loopback run's stop. A
/// terminal's <c>dotnet run</c> gets the same. Nothing changes without a parent console (F5 in an IDE, Explorer, a
/// shortcut) or for a process that already has one (an <c>Exe</c> app, a test process): attaching then fails and no
/// handler is registered. No console window is created: attaching joins the parent's own, visible or not. The app's
/// standard handles stay the ones it inherited (<c>dotnet run</c> hands them down), so its output and input, and the
/// development server's input (<c>DevServerLauncher</c>), are unchanged; the server runs in a console of its own
/// (<c>CreateNoWindow</c>), so the Ctrl+C never reaches it and the next run reuses it.
/// <para>
/// Development builds only: <see cref="Listen"/> is called behind <see cref="SparDevelopment.IsSupported"/>, which a
/// Production build turns off and trims, so a shipped app never attaches to a console.
/// </para>
/// </summary>
internal static partial class ConsoleInterrupt
{
    private const uint AttachParentProcess = unchecked((uint)-1);

    /// <summary>
    /// Attaches to the parent's console when this process has none and the parent has one, and from then calls
    /// <paramref name="interrupted"/> on the console's Ctrl+C and Ctrl+Break, on the signal's thread. It returns whether
    /// it handled the interrupt; when it did not (nothing to close yet), the default handling ends the process. Returns
    /// null, with nothing changed, off Windows, outside Development, or when there is no console to attach to.
    /// </summary>
    public static IDisposable? Listen(HostLog log, Func<bool> interrupted)
    {
        if (!TryAttach(log))
        {
            return null;
        }

        void Interrupted(PosixSignalContext context)
        {
            var handled = interrupted();
            context.Cancel = handled;
            log.Info($"{(context.Signal == PosixSignal.SIGINT ? "Ctrl+C" : "Ctrl+Break")} from the parent's console: {(handled ? "the app closes" : "nothing to close yet, the process ends")}.");
        }

        return new Registrations(
            PosixSignalRegistration.Create(PosixSignal.SIGINT, Interrupted),
            PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Interrupted));
    }

    /// <summary>
    /// Only the attach, for a host that answers the console's signals itself (the loopback run's own SIGINT, SIGTERM
    /// and SIGQUIT): whether this process attached to its parent's console.
    /// </summary>
    public static bool TryAttach(HostLog log) =>
        SparDevelopment.IsSupported && OperatingSystem.IsWindows() && Attach(log);

    [SupportedOSPlatform("windows")]
    private static bool Attach(HostLog log)
    {
        if (!AttachConsole(AttachParentProcess))
        {
            // ERROR_ACCESS_DENIED: this process has a console already; ERROR_INVALID_HANDLE: the parent has none.
            return false;
        }

        // A process started in a new process group (as dotnet watch starts dotnet run) inherits "ignore Ctrl+C"; the
        // console's Ctrl+C is what this attach is for.
        SetConsoleCtrlHandler(0, false);
        log.Info("Attached to the parent's console (Development): its Ctrl+C closes the app as its window's close does.");
        return true;
    }

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(nint handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    private sealed class Registrations(params PosixSignalRegistration[] registrations) : IDisposable
    {
        public void Dispose()
        {
            foreach (var registration in registrations)
            {
                registration.Dispose();
            }
        }
    }
}
