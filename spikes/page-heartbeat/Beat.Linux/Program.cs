using System.Globalization;
using System.Runtime.Versioning;
using Spar.PageHeartbeat;

[assembly: SupportedOSPlatform("linux")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return LinuxHost.Run(new Options(args));
