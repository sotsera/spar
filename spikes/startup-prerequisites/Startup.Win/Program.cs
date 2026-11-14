using Microsoft.Win32;

namespace Startup.Win;

// Exit codes: 0 ready, 2 usage, 3 missing, 4 too old, 5 broken, 7 OS too old (6, no display, is Linux only).
internal static class Program
{
    // WebView2 needs an STA. Without [STAThread] the runtime puts the main thread in the MTA first, and environment
    // creation fails with RPC_E_CHANGED_MODE (0x80010106).
    [STAThread]
    private static int Main(string[] args)
    {
        Options o;
        try
        {
            o = Options.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        using var log = new Log(o.LogFile);
        var theme = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
        log.Mark("main", ("label", o.Label), ("ui", o.Ui), ("stage", o.Stage.ToString()), ("appsUseLightTheme", theme?.ToString()),
            ("threadAwareness", Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext())),
            ("apartment", Thread.CurrentThread.GetApartmentState().ToString()),
#if NO_MANIFEST
            ("manifest", "none"));
#else
            ("manifest", "full"));
#endif

        var problem = new Checks(o, log).Run();
        if (problem is null)
        {
            log.Mark("ready", ("exit", 0));
            return 0;
        }

        log.Mark("problem", ("kind", problem.Kind.ToString()), ("exit", problem.ExitCode), ("details", problem.Details));
        Report.Show(problem, o, log);
        log.Mark("exit", ("code", problem.ExitCode));
        return problem.ExitCode;
    }
}
