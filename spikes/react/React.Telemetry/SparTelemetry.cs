using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace React.Telemetry;

/// <summary>
/// Spike of the optional OpenTelemetry layer (a future Spar.OpenTelemetry package). It does nothing unless the
/// standard OTEL_EXPORTER_OTLP_ENDPOINT variable is set: the app author, or a tool like Aspire, chooses the endpoint.
/// Exports Spar's traces, metrics and logs over OTLP; the exporter reads the other OTEL_* variables itself.
/// </summary>
public sealed class SparTelemetry : IDisposable
{
    private readonly TracerProvider _tracer;
    private readonly MeterProvider _meter;
    private readonly ILoggerFactory _loggers;

    private SparTelemetry(string serviceName, string sourceName)
    {
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);
        _tracer = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource).AddSource(sourceName).AddOtlpExporter().Build();
#if SPIKE_TRACES_ONLY
        _meter = null!;
#else
        _meter = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource).AddMeter(sourceName).AddOtlpExporter().Build();
#endif
#if SPIKE_NO_LOGS || SPIKE_TRACES_ONLY
        _loggers = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
#else
        _loggers = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resource);
            options.AddOtlpExporter();
        }));
#endif
        Logger = _loggers.CreateLogger(sourceName);
    }

    public ILogger Logger { get; }

    /// <summary>One structured log record per bridge call.</summary>
    public void LogCall(string contract, string command, double elapsedMs, bool ok) =>
        SparLog.Call(Logger, contract, command, ok ? "ok" : "failed", Math.Round(elapsedMs, 2));

    /// <summary>Starts exporting when an OTLP endpoint is configured; otherwise returns null and costs nothing more.</summary>
    public static SparTelemetry? StartIfConfigured(string serviceName, string sourceName) =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"))
            ? null
            : new SparTelemetry(serviceName, sourceName);

    public void Dispose()
    {
        // Flushes pending batches.
        _tracer.Dispose();
        _meter?.Dispose();
        _loggers.Dispose();
    }
}

/// <summary>Source-generated log messages (no boxing, AOT-safe).</summary>
internal static partial class SparLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Contract}.{Command} {Outcome} in {ElapsedMs} ms")]
    public static partial void Call(ILogger logger, string contract, string command, string outcome, double elapsedMs);
}
