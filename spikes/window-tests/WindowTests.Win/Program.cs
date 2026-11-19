using System.Globalization;
using System.Runtime.Versioning;
using Spar.WindowTests;

[assembly: SupportedOSPlatform("windows10.0")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: WindowTests.Win profiles|parallel|crash|check|input|filechooser [--options]");
    return 64;
}

var options = new Options(args[1..]);
var timeout = options.Int("timeout", 600);
return args[0] switch
{
    "profiles" => Ui.Run(() => ProfileTests.RunAsync(options), timeout),
    "input" => Ui.Run(() => InputTests.RunAsync(options), timeout),
    "filechooser" => Ui.Run(() => FileChooserTests.RunAsync(options), timeout),
    "parallel" => Harness.Parallel(options),
    "crash" => Harness.CrashAsync(options).GetAwaiter().GetResult(),
    "check" => Harness.Check(options),
    _ => 64,
};
