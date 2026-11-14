// The desktop app (AOT, telemetry layer, offscreen self-test) as an executable resource. WithOtlpExporter gives it the
// OTEL_EXPORTER_OTLP_* variables Aspire normally injects (endpoint, protocol, API key header, service name), so the
// backend exports directly and the page's exports go through the host's relay with the same settings.
var builder = DistributedApplication.CreateBuilder(args);
var app = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "react", "React.App", "bin", "publish-telemetry-selftest"));

builder.AddExecutable("spar-notes-desktop", Path.Combine(app, "React.App.exe"), app, "--selftest", "--offscreen", "--telemetry")
    .WithOtlpExporter();

builder.Build().Run();
