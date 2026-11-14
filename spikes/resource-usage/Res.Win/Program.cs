using System.Globalization;
using System.Runtime.Versioning;
using Spar.ResourceUsage;

[assembly: SupportedOSPlatform("windows10.0")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return args switch
{
    ["host", .. var rest] => Host.Run(new Options(rest)),
    ["walk", .. var rest] => WalkScale.Run(new Options(rest), ("FindFirstFileEx", Win32.FindWalk), ("FileFullDirectoryInfo", Win32.DirectoryInfoWalk)),
    _ => 64,
};
