using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Spar.Hosting;
using Spar.OpenTelemetry;

namespace Spar;

/// <summary>The optional OpenTelemetry layer (<c>Spar.OpenTelemetry</c>), turned on in <c>Program.cs</c>.</summary>
public static class SparTelemetryExtensions
{
    /// <summary>The name of Spar's activity source and meter: each bridge call's span and <c>spar.call.duration</c>.</summary>
    public const string SourceName = "Spar";

    /// <summary>
    /// Exports the app's traces, metrics and logs over OTLP, and relays the page's spans to the same endpoint, when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set; without it nothing is exported, and the page loads no telemetry code.
    /// Spar's own activity source and meter (<see cref="SourceName"/>) and every <c>ILogger</c> line, at the log file's
    /// levels, are always in; <paramref name="configure"/> adds the app's own, such as HttpClient's or SqlClient's
    /// instrumentation. OpenTelemetry's standard variables apply: <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>,
    /// <c>OTEL_EXPORTER_OTLP_HEADERS</c>, <c>OTEL_SERVICE_NAME</c> (the page reports as its name with <c>-page</c>),
    /// <c>OTEL_RESOURCE_ATTRIBUTES</c>, <c>OTEL_TRACES_SAMPLER</c> and the others.
    /// <code>
    /// spar.Telemetry(otel => otel.WithTracing(tracing => tracing.AddHttpClientInstrumentation()));
    /// </code>
    /// </summary>
    public static SparBuilder Telemetry(this SparBuilder spar, Action<IOpenTelemetryBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(spar);
        var settings = OtlpSettings.Read(Environment.GetEnvironmentVariable, spar.AppId);
        var relay = new OtlpRelay(settings);
        spar.TelemetryRelay = relay;
        spar.Services.AddSingleton(relay);
        if (!settings.Enabled)
        {
            return spar;
        }

        var otel = spar.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(settings.ServiceName))
            .WithTracing(tracing => tracing.AddSource(SourceName).AddOtlpExporter())
            .WithMetrics(metrics => metrics.AddMeter(SourceName).AddOtlpExporter())
            .WithLogging(logging => logging.AddOtlpExporter());
        configure?.Invoke(otel);

        // No generic host starts the providers here: they start when the app's services are built.
        spar.Services.AddSingleton(new SparStartup(services =>
        {
            _ = services.GetService<TracerProvider>();
            _ = services.GetService<MeterProvider>();
            _ = services.GetService<LoggerProvider>();
        }));
        return spar;
    }
}
