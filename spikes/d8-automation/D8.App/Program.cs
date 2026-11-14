using System.Diagnostics;

namespace D8.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var clock = Stopwatch.StartNew();
        var options = Options.Parse(args);
#if SPAR_DEVTOOLS
        const string devtools = "compiled";
#else
        const string devtools = "excluded";
#endif
        Console.WriteLine(
            $"pid={Environment.ProcessId} apartment={Thread.CurrentThread.GetApartmentState()} " +
            $"devtools={devtools} aot={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled} " +
            $"mode={options.Mode} window={options.Window}");
        return Host.Run(options, clock, RunScenarioAsync);
    }

    private static async Task RunScenarioAsync(Host host)
    {
        if (host.Options.Mode == "smoke")
        {
            Console.WriteLine("smoke: navigation completed");
            host.Close();
            return;
        }

#if SPAR_DEVTOOLS
        if (Devtools.DevtoolsFeature.IsSupported)
        {
            await Devtools.DevtoolsEntry.RunAsync(host);
            return;
        }
#endif

        await Task.CompletedTask;
        if (host.Options.Mode != "show")
        {
            Console.WriteLine("Devtools are not available in this build.");
            host.ExitCode = 3;
            host.Close();
        }
    }
}
