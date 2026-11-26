namespace Spar;

/// <summary>Stand-in for the optional OpenTelemetry layer's call (3.18; the real one is in Spar.OpenTelemetry).</summary>
public static class SparTelemetryExtensions
{
    /// <summary>Exports traces, metrics and logs over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set.</summary>
    public static SparBuilder Telemetry(this SparBuilder spar, Action<object>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(spar);
        _ = configure;
        return spar;
    }
}
