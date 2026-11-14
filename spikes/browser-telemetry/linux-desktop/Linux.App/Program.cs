using Microsoft.Extensions.DependencyInjection;
using React.App;
using Spar.Generated;

// The React spike's backend (notes, links, lab), unchanged, hosted by the Linux desktop host.
var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<INoteStore>(new InMemoryNoteStore());
services.AddSingleton<NotesService>();
services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
services.AddSingleton<LinksService>();
services.AddSingleton<LabService>();
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
object Resolve(Type type) => provider.GetRequiredService(type);

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This spike host runs on Linux only.");
    return 1;
}

// The page's trace context reaches SparDiagnostics, which makes the command's Activity its child.
var invoke = SparDiagnostics.Instrument((contract, command, payload, _, ct) => SparCatalog.InvokeAsync(Resolve, contract, command, payload, ct));
Spike.ISparTelemetryRelay? pageTelemetry = null;
#if SPAR_TELEMETRY
// Optional layer: exports over OTLP and relays the page's exports, only when OTEL_EXPORTER_OTLP_ENDPOINT is set.
using var telemetry = React.Telemetry.SparTelemetry.StartIfConfigured("spar-notes-linux", SparDiagnostics.Name);
if (telemetry is not null)
{
    SparDiagnostics.CallCompleted = telemetry.LogCall;
    pageTelemetry = telemetry.Relay;
}
#endif

return Linux.Desktop.LinuxHost.Run(
    new(
        "Spar notes",
        invoke,
        SparCatalog.Manifest,
        typeof(NotesService).Assembly,
        (contract, command, payload, ct) => SparCatalog.OpenStream(Resolve, contract, command, payload, ct),
        pageTelemetry),
    args);
