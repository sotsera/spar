using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// How the Linux host reports a <see cref="StartupProblem"/> (plan section 3, "Missing prerequisites", Linux message):
/// the line on standard error and in the per-user log, always; then, unless the run is automated, the app's hook
/// (<c>spar.OnStartupProblem</c>), and Spar's message when the hook showed none: GTK 4's <c>GtkAlertDialog</c>, drawn
/// with cairo unless the user chose a renderer, where GTK 4 can be used; otherwise the first of kdialog (first on KDE),
/// zenity and xmessage that runs. With no graphical session nothing graphical is tried. The exit code is the problem's,
/// whatever happens. Everything here runs on the process's main thread, before the app's window exists, so waiting
/// for the message blocks nothing that draws.
/// </summary>
internal static class StartupFailure
{
    /// <summary>Reports <paramref name="problem"/> and returns its exit code.</summary>
    public static int Report(StartupProblem problem, StartupContext context, bool gtkUsable)
    {
        var line = StartupReport.Describe(problem);
        context.Log.Error(line);
        if (context.Automated)
        {
            context.Log.Info("The automated mode shows no start-up message.");
            return problem.ExitCode;
        }

        if (StartupReport.AppShowedItsOwnMessage(context.Hook, problem, (message, exception) => context.Log.Error(message, exception)))
        {
            context.Log.Info("The app showed its own start-up message.");
            return problem.ExitCode;
        }

        if (problem.Kind == StartupProblemKind.NoGraphicalSession)
        {
            return problem.ExitCode;
        }

        var heading = $"{context.Title} can't start";
        var detail = $"{problem.Details}\n\nExit code {problem.ExitCode}." + (context.Log.FilePath is { } log ? $" Log: {log}" : "");
        var shown = gtkUsable ? ShowWithGtk(context, heading, detail) : ShowWithProgram(heading, detail, context.Log);
        context.Log.Info(shown ? "The start-up message was closed." : "No start-up message could be shown.");
        return problem.ExitCode;
    }

    /// <summary>
    /// The message in a <c>GtkAlertDialog</c> of its own on a main loop of its own, which ends when the dialog is
    /// answered. Cairo draws it unless the user set <c>GSK_RENDERER</c>: GTK's GL renderer on a software GPU shows it
    /// 3–6 times later. False when GTK cannot open the display.
    /// </summary>
    private static bool ShowWithGtk(StartupContext context, string heading, string detail)
    {
        Libc.SetEnv(GpuCheck.RendererVariable, "cairo", overwrite: 0);
        var shown = false;
        MainLoop.Run(
            exception => context.Log.Error("The start-up message failed", exception),
            ui =>
            {
                GLib.g_set_prgname(context.Identity);
                if (Gtk.gtk_init_check() == 0)
                {
                    return false;
                }

                shown = true;
                _ = WaitAsync(ui, AlertDialog.ChooseAsync(0, heading, detail, "Close"));
                return true;
            });
        return shown;
    }

    private static async Task WaitAsync(MainLoop ui, Task<int> dialog)
    {
        try
        {
            await dialog;
        }
        finally
        {
            ui.Quit();
        }
    }

    /// <summary>
    /// Without GTK 4: a message program, the first that runs (plan section 3: kdialog, first on KDE, then zenity, then
    /// xmessage). zenity needs GTK 4 on current distributions, so it rarely helps here; a program whose libraries are
    /// missing exits 127 and the next is tried.
    /// </summary>
    private static bool ShowWithProgram(string heading, string detail, HostLog log)
    {
        var text = $"{heading}\n\n{detail}";
        var kde = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE", StringComparison.OrdinalIgnoreCase);
        var programs = new List<(string File, string[] Arguments)>
        {
            ("zenity", ["--error", "--no-markup", "--title", heading, "--text", detail]),
            ("xmessage", ["-center", "-title", heading, "-buttons", "Close:0", "-default", "Close", text]),
        };
        programs.Insert(kde ? 0 : 1, ("kdialog", ["--title", heading, "--error", detail]));
        foreach (var (file, arguments) in programs)
        {
            var result = ChildProcess.Run(file, arguments, captureOutput: false, timeoutMilliseconds: null);
            if (result.Started && result.ExitCode is not (126 or 127))
            {
                log.Info($"The start-up message was shown with {file}.");
                return true;
            }
        }

        return false;
    }
}

/// <summary>What a start-up report needs from the run: the app's title and identity, its hook, the log and the automated mode.</summary>
internal sealed record StartupContext(string Title, string Identity, Func<StartupProblem, bool>? Hook, HostLog Log, bool Automated);
