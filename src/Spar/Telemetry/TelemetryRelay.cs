namespace Spar.Telemetry;

/// <summary>
/// The optional OpenTelemetry layer's relay (section 6b; decision "M2d's decide-first questions", items 3 and 4), which
/// <c>Spar.OpenTelemetry</c> registers in the app's services: the page's spans, posted to its own origin, go through it
/// unchanged to the configured OTLP endpoint. Without it, or without an endpoint, the hosts answer the page's
/// <c>/_spar/otlp/*</c> requests with 404 and the page loads no telemetry code.
/// </summary>
internal interface ITelemetryRelay
{
    /// <summary>Whether an endpoint is configured (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>): the page's telemetry is on.</summary>
    bool Enabled { get; }

    /// <summary>The page's service name: the app's (<c>OTEL_SERVICE_NAME</c>, else the app's own) with <c>-page</c>.</summary>
    string PageServiceName { get; }

    /// <summary>How long the page's batch processor waits between exports (<c>OTEL_BSP_SCHEDULE_DELAY</c>, else 5000).</summary>
    int PageDelayMilliseconds { get; }

    /// <summary>
    /// Sends one OTLP traces request from the page, its bytes unchanged, to the endpoint; the answer is the endpoint's
    /// status (503 at once for 5 s after a connection failure). Never replays a request.
    /// </summary>
    Task<TelemetryRelayAnswer> ForwardTracesAsync(ReadOnlyMemory<byte> body, string contentType, CancellationToken cancellationToken);
}

/// <summary>The relay's answer to the page: the status the endpoint gave, or the relay's own.</summary>
internal readonly record struct TelemetryRelayAnswer(int Status, string Reason);
