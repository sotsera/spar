using Spar;
using Spar.Harness;

// A small message limit, so the script can send one message over it without building megabytes. The stall probe is
// kept although the harness has no window: the build tests check that this call is what keeps it in a Production
// image, which the Notebook, without the call, does not carry.
var app = SparApplication.Create(args, spar => spar
    .Bridge(o => o.MaxMessageBytes = ConsoleHost.MaxMessageBytes)
    .Diagnostics(d => d.KeepStallProbe()));
return ConsoleHost.Run(app, args);
