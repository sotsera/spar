using BinaryData;
using Microsoft.Extensions.DependencyInjection;
using Spar;

var app = SparApplication.Create(args, spar =>
{
    spar.Window(w => w.Size(900, 600));

    // Room for 32 MB each way as base64 (about 43 MB of JSON), so the bridge path can be measured past its defaults.
    spar.Bridge(o =>
    {
        o.MaxResultBytes = 96 * 1024 * 1024;
        o.MaxMessageBytes = 48 * 1024 * 1024;
        o.QueuedBytes = 96 * 1024 * 1024;
    });

    spar.Services.AddSingleton<BenchResults>();
});

return app.Run();
