#if !SPIKE_NO_DI
using Microsoft.Extensions.DependencyInjection;
#endif
using React.App;
using Spar.Generated;

#if SPIKE_NO_DI
// Size probe only (Release): no container, one hand-made instance.
var notesService = new NotesService(TimeProvider.System, new InMemoryNoteStore());
var linksService = new LinksService(new HttpClient());
object Resolve(Type type) => type == typeof(LinksService) ? linksService : notesService;
#else
var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<INoteStore>(
#if SPAR_SQL
    // A connection string (from Aspire, or set by hand) moves the notes to SQL Server.
    Environment.GetEnvironmentVariable("ConnectionStrings__notes") is { Length: > 0 } notesDatabase ? new SqlNoteStore(notesDatabase) :
#endif
    new InMemoryNoteStore());
services.AddSingleton<NotesService>();
services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
services.AddSingleton<LinksService>();
services.AddSingleton<LabService>();

#if SPAR_LOOPBACK
// In-process test factory (spike hook): the test's service overrides run after the app's registrations.
var testHooks = Loop.Loopback.SparTestHooks.Current;
testHooks?.ConfigureServices?.Invoke(services);
#endif

await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
object Resolve(Type type) => provider.GetRequiredService(type);
#endif

// Every bridge call goes through Spar's always-on diagnostics (BCL Activity and Meter, no cost without a listener).
// The page's trace context reaches SparDiagnostics, which makes the command's Activity its child.
var invoke = SparDiagnostics.Instrument((contract, command, payload, _, ct) => SparCatalog.InvokeAsync(Resolve, contract, command, payload, ct));
Spike.ISparTelemetryRelay? pageTelemetry = null;
Spike.SparStream OpenStream(string contract, string command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
    SparCatalog.OpenStream(Resolve, contract, command, payload, cancellationToken);
#if SPAR_TELEMETRY
// Optional layer: exports traces, metrics and logs over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set.
using var telemetry = React.Telemetry.SparTelemetry.StartIfConfigured("spar-notes", SparDiagnostics.Name);
if (telemetry is not null)
{
    SparDiagnostics.CallCompleted = telemetry.LogCall;

    // The page exports to /_spar/otlp/…; the host hands those requests to this relay (404 when it is null).
    pageTelemetry = telemetry.Relay;
}
#endif

#if SPAR_LOOPBACK
if (testHooks is not null)
{
    testHooks.Services = provider;
}

var projectDirectory = typeof(NotesService).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
    .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "SparProjectDirectory")?.Value;

if (args.Contains("--loopback"))
{
#if SPAR_DEV_SERVER_BUILD
    // A development build embeds no bundle: the dev server serves the page. It starts beside the backend and reads
    // the backend address from the dev session file.
    if (projectDirectory is not null && testHooks is null)
    {
        _ = DevServer.EnsureAsync(projectDirectory);
    }
#endif

    return await Loop.Loopback.LoopbackHost.RunAsync(
        new(
            invoke,
            SparCatalog.Manifest,
            () => typeof(NotesService).Assembly.GetManifestResourceStream("Spar.Frontend")!,
            projectDirectory,
            OpenStream,
            pageTelemetry),
        args);
}
#endif

#if SPIKE_NO_HOST
// Size probe only: dispatch without any host.
var count = await SparCatalog.InvokeAsync(Resolve, "notes", "count", default, CancellationToken.None);
Console.WriteLine(System.Text.Encoding.UTF8.GetString(count));
return 0;
#else
if (!OperatingSystem.IsWindowsVersionAtLeast(10))
{
    Console.Error.WriteLine("The desktop host needs Windows 10 or later; use --loopback elsewhere.");
    return 1;
}

return React.Desktop.DesktopHost.Run(
    new(
        "Spar notes",
        invoke,
        SparCatalog.Manifest,
        () => typeof(NotesService).Assembly.GetManifestResourceStream("Spar.Frontend")!,
#if SPAR_DEV_SERVER_BUILD
        projectDirectory is null ? null : () => DevServer.EnsureForDesktopAsync(projectDirectory),
        OpenStream,
        pageTelemetry),
#else
        null,
        OpenStream,
        pageTelemetry),
#endif
    args);
#endif
