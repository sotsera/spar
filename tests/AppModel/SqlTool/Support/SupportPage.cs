using System.ComponentModel;
using System.Diagnostics;
using Spar;

namespace SqlTool.Support;

/// <summary>The install guide the company's IT department keeps, for what users cannot fix themselves.</summary>
public static class SupportPage
{
    private const string Guide = "https://intranet.example.com/sqltool/install";

    /// <summary>Opens the guide for a missing or outdated prerequisite; true when it opened, so Spar shows no message.</summary>
    public static bool TryOpen(StartupProblem problem)
    {
        if (problem.Kind is not (StartupProblemKind.Missing or StartupProblemKind.TooOld or StartupProblemKind.OsTooOld))
        {
            return false;
        }

        try
        {
            var page = $"{Guide}?component={Uri.EscapeDataString(problem.Component)}";
            Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
