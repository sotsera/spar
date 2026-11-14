using System.Globalization;
using System.Runtime.Versioning;
using Spar.WebViewProfiles;

[assembly: SupportedOSPlatform("windows10.0")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: Profiles.Win host [--options] | harness <shutdown|relaunch|concurrent|clean> [--options]");
    return 64;
}

return args[0] switch
{
    "host" => Host.Run(new Options(args[1..])),
    "harness" => Harness.Run(args[1], new Options(args[2..])),
    _ => 64,
};
