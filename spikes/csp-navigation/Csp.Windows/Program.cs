using Csp.Shared;
using Csp.Windows;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows10.0")]

var options = new HostOptions(args);
return options.Has("--loopback") ? LoopbackHost.Run(options) : DesktopHost.Run(options);
