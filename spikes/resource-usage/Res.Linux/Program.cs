using System.Globalization;
using System.Runtime.Versioning;
using Spar.ResourceUsage;

[assembly: SupportedOSPlatform("linux")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// No inspector servers, ever (plan section 3c).
Environment.SetEnvironmentVariable("WEBKIT_INSPECTOR_SERVER", null);
Environment.SetEnvironmentVariable("WEBKIT_INSPECTOR_HTTP_SERVER", null);
return args switch
{
    ["host", .. var rest] => LinuxHost.Run(new Options(rest)),
    ["walk", .. var rest] => WalkScale.Run(new Options(rest), ("lstat-blocks", LinuxWalk.Allocated)),
    _ => 64,
};
