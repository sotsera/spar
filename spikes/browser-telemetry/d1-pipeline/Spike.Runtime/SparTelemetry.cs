namespace Spike;

/// <summary>Runs one unary command for a host. <paramref name="trace"/> is the page's trace context, if it sent one.</summary>
public delegate ValueTask<byte[]> SparInvoke(string contract, string command, ReadOnlyMemory<byte> payload, SparTraceContext trace, CancellationToken cancellationToken);

/// <summary>
/// The W3C trace context a page sent with a call (<c>traceparent</c> and optional <c>tracestate</c>), unvalidated.
/// Hosts copy it from the headers of a fetch call or from the trace field of a channel message; the diagnostics
/// layer parses it with <c>ActivityContext.TryParse</c>, which rejects malformed values.
/// </summary>
public readonly record struct SparTraceContext(string? TraceParent, string? TraceState)
{
    // W3C limits: traceparent version 00 is 55 characters; tracestate at most 32 members of up to 256 characters.
    private const int MaxTraceParent = 128;
    private const int MaxTraceState = 8192;

    public bool IsEmpty => TraceParent is null;

    /// <summary>From fetch headers. Oversized values are dropped, not truncated.</summary>
    public static SparTraceContext FromHeaders(string? traceParent, string? traceState) =>
        traceParent is { Length: > 0 and <= MaxTraceParent }
            ? new(traceParent, traceState is { Length: > 0 and <= MaxTraceState } ? traceState : null)
            : default;

    /// <summary>From a channel message's trace field: <c>-</c>, or traceparent[;escaped tracestate].</summary>
    public static SparTraceContext FromField(string field)
    {
        if (field.Length == 0 || field == "-")
        {
            return default;
        }

        var separator = field.IndexOf(';', StringComparison.Ordinal);
        return separator < 0
            ? FromHeaders(field, null)
            : FromHeaders(field[..separator], Uri.UnescapeDataString(field[(separator + 1)..]));
    }
}

/// <summary>
/// Optional: forwards the page's OTLP/HTTP exports (<c>POST /_spar/otlp/v1/{signal}</c>) to the app's configured
/// collector. The optional telemetry layer provides it; without one, hosts answer those paths with 404 and the page
/// never loads its telemetry code. Hosts check origin or token before calling it, exactly as for bridge calls.
/// </summary>
public interface ISparTelemetryRelay
{
    /// <summary>The page SDK's settings (JSON), returned by <c>POST /_spar/otlp/config</c>.</summary>
    ReadOnlyMemory<byte> PageConfig { get; }

    /// <summary>Forwards one export request body unchanged. Never throws for collector failures; returns the status for the page.</summary>
    Task<SparRelayResponse> ForwardAsync(string signal, ReadOnlyMemory<byte> body, string? contentType, CancellationToken cancellationToken);
}

/// <summary>What the page's exporter receives back.</summary>
public readonly record struct SparRelayResponse(int Status, ReadOnlyMemory<byte> Body, string ContentType);

/// <summary>Host-side routing for <c>/_spar/otlp/…</c>, shared by every host.</summary>
public static class SparTelemetryRoutes
{
    public const string Prefix = "/_spar/otlp/";

    /// <summary>A page export larger than this is refused (413) before it is read into the relay.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    public static bool IsSignal(string signal) => signal is "traces" or "logs" or "metrics";

    public static bool IsOtlpContentType(string? contentType) =>
        contentType is not null
        && (contentType.StartsWith("application/x-protobuf", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Answers one request under <see cref="Prefix"/>: 404 when telemetry is off, the page config, or the relayed export.
    /// <paramref name="path"/> is the URL path; the caller has already checked the method, origin and token.
    /// </summary>
    public static async Task<SparRelayResponse> HandleAsync(
        ISparTelemetryRelay? relay, string path, ReadOnlyMemory<byte> body, string? contentType, CancellationToken cancellationToken)
    {
        if (relay is null)
        {
            return Error(404, "telemetry-disabled");
        }

        var rest = path[Prefix.Length..];
        if (rest == "config")
        {
            return new(200, relay.PageConfig, "application/json");
        }

        if (!rest.StartsWith("v1/", StringComparison.Ordinal) || !IsSignal(rest[3..]))
        {
            return Error(404, "unknown-signal");
        }

        if (body.Length > MaxBodyBytes)
        {
            return Error(413, "too-large");
        }

        if (!IsOtlpContentType(contentType))
        {
            return Error(415, "unsupported-content-type");
        }

        return await relay.ForwardAsync(rest[3..], body, contentType, cancellationToken).ConfigureAwait(false);
    }

    private static SparRelayResponse Error(int status, string code) =>
        new(status, SparMessage.ErrorJson(code, null), "application/json");
}
