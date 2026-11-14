// Starts the app with KillOnParentExit and waits, so a script can kill this process and check the app went too.
using System.Diagnostics;

var kill = args.Length < 2 || args[1] != "off";
string[] appArgs = args.Length > 2 && args[2] == "nostdin" ? ["--loopback"] : ["--loopback", "--exit-with-stdin"];
var startInfo = new ProcessStartInfo(args[0], appArgs)
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
};
if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
{
    startInfo.KillOnParentExit = kill;
}

using var app = Process.Start(startInfo)!;
Console.WriteLine($"parent={Environment.ProcessId} app={app.Id} kill-on-parent-exit={kill}/{string.Join('+', appArgs)}");
Console.Out.Flush();
Thread.Sleep(Timeout.Infinite);
