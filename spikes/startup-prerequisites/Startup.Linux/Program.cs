namespace Startup.Linux;

// Exit codes: 0 ready, 2 usage, 3 missing, 4 too old, 5 broken, 6 no display (Windows adds 7, OS too old).
internal static class Program
{
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
        log.Mark("main", ("label", o.Label), ("ui", o.Ui), ("processAgeMs", Log.ProcessAgeMs()), ("uid", LibC.getuid()),
            ("desktop", Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")));
        var checks = new Checks(o, log);
        var problem = checks.Run();
        if (problem is null && o.TryView)
        {
            problem = TryView.Run(o, log);
        }

        if (problem is null)
        {
            log.Mark("ready", ("exit", 0));
            return 0;
        }

        log.Mark("problem", ("kind", problem.Kind.ToString()), ("exit", problem.ExitCode), ("details", problem.Details));
        Report.Show(problem, o, checks, log);
        log.Mark("exit", ("code", problem.ExitCode));
        return problem.ExitCode;
    }
}
