using Spar;
using Spar.Harness;

// A small message limit, so the script can send one message over it without building megabytes.
var app = SparApplication.Create(args, spar => spar.Bridge(o => o.MaxMessageBytes = ConsoleHost.MaxMessageBytes));
return ConsoleHost.Run(app, args);
