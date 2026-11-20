using Spike.BlackBoxAtSpi;
using Spike.BlackBoxAtSpi.Runs;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

// The spike's verbs (wsl.sh `run <bus> <verb> [args]` starts the reader inside the run's Xvfb and D-Bus session).
Console.Out.Flush();
try
{
    return args.FirstOrDefault() switch
    {
        "env" => EnvironmentRun.Run(args.Skip(1).ToArray()),
        "window" => WindowRun.Run(args.Skip(1).ToArray()),
        "relaunch" => DialogRun.Relaunch(args.Skip(1).ToArray()),
        "hang" => DialogRun.Hang(args.Skip(1).ToArray()),
        "kill" => KillRun.Run(args.Skip(1).ToArray()),
        "holder" => KillRun.Holder(args.Skip(1).ToArray()),
        "dispose" => KillRun.Dispose(args.Skip(1).ToArray()),
        _ => Usage(),
    };
}
catch (Exception exception)
{
    Console.WriteLine($"FATAL {exception}");
    return 1;
}

static int Usage()
{
    Console.WriteLine("verbs: env <variant>, window <runs>, relaunch <runs>, hang <runs>, kill <variant> <delays> <runs>, dispose <mode> <runs>");
    return 2;
}
