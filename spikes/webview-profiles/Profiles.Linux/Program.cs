using System.Globalization;
using System.Runtime.Versioning;
using Spar.WebViewProfiles;

[assembly: SupportedOSPlatform("linux")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: Profiles.Linux host [--options] | harness <shutdown|relaunch|concurrent|clean> [--options]");
    return 64;
}

// No inspector servers, ever (plan section 3c).
Environment.SetEnvironmentVariable("WEBKIT_INSPECTOR_SERVER", null);
Environment.SetEnvironmentVariable("WEBKIT_INSPECTOR_HTTP_SERVER", null);
return args[0] switch
{
    "host" => LinuxHost.Run(new Options(args[1..])),
    "harness" => LinuxHarness.Run(args[1], new Options(args[2..])),
    _ => 64,
};
