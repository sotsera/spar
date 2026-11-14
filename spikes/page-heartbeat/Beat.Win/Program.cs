using System.Globalization;
using System.Runtime.Versioning;
using Spar.PageHeartbeat;

[assembly: SupportedOSPlatform("windows10.0")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return Host.Run(new Options(args));
