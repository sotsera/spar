namespace Spar.Hosting;

/// <summary>
/// What every host does with a <see cref="StartupProblem"/> before it shows its own message (plan section 3, "Missing
/// prerequisites"): the line for standard error and the log, and the app's hook (<c>spar.OnStartupProblem</c>), which
/// may replace the message but never the exit code.
/// </summary>
internal static class StartupReport
{
    /// <summary>
    /// The oldest Windows Spar supports: Windows 10 22H2, build 19045 (user decision, 2026-11-17, provisional; M4
    /// confirms it on a real Windows 10). Features of Windows 11 are detected at run time.
    /// </summary>
    public const int MinimumWindowsBuild = 19045;

    /// <summary>One line for standard error and the per-user log, with everything a support person needs.</summary>
    public static string Describe(StartupProblem problem)
    {
        var versions = (problem.Found, problem.Required) switch
        {
            (null, null) => "",
            (var found, null) => $" (found {found})",
            (null, var required) => $" (required {required})",
            var (found, required) => $" (found {found}, required {required})",
        };
        var help = problem.HelpUrl is null ? "" : $" Help: {problem.HelpUrl}";
        return $"Start-up problem {problem.Kind}: {problem.Component}{versions}. {problem.Details}{help} Exit code {problem.ExitCode}.";
    }

    /// <summary>
    /// Runs the app's hook; true when it showed the app's own message. A hook that throws counts as false, so the
    /// user still sees Spar's message, and its exception goes to <paramref name="logError"/>.
    /// </summary>
    public static bool AppShowedItsOwnMessage(Func<StartupProblem, bool>? hook, StartupProblem problem, Action<string, Exception> logError)
    {
        if (hook is null)
        {
            return false;
        }

        try
        {
            return hook(problem);
        }
        catch (Exception exception)
        {
            logError("The app's start-up hook failed; Spar shows its own message", exception);
            return false;
        }
    }
}
