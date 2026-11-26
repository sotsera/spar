using System.Diagnostics;
using System.Globalization;
using System.Text;
using Spar.Hosting;

namespace Spar.Telemetry;

/// <summary>
/// The page's telemetry routes on the app's origin, the same on every host (section 6b; decision "M2d's decide-first
/// questions", item 4): <c>POST /_spar/otlp/v1/traces</c> to the relay, and <c>POST /_spar/otlp/config</c> for loopback's
/// page, which has no host-injected script. Every other <c>/_spar/otlp/*</c> request, and every one without the layer or
/// an endpoint, is a 404, except the configuration's <c>POST</c>, which is then a 204 without settings: a page that asks
/// on every load logs no failed request in its console while telemetry is off. A traces request is refused unless it is protobuf or JSON (415), up to <see cref="MaxBody"/>
/// bytes (413), and within <see cref="PerSecond"/> a second for the window or loopback page (429). The hosts run these
/// checks where they answer requests; the forward itself runs off that thread.
/// </summary>
internal sealed class TelemetryRoute
{
    public const string Prefix = "/_spar/otlp/";
    public const string TracesPath = "/_spar/otlp/v1/traces";
    public const string ConfigPath = "/_spar/otlp/config";

    /// <summary>The largest traces request the relay forwards.</summary>
    public const int MaxBody = 1024 * 1024;

    /// <summary>The traces requests a page may send a second (and at once).</summary>
    public const int PerSecond = 20;

    private readonly ITelemetryRelay? _relay;
    private readonly ReportLimiter _limiter = new(PerSecond, PerSecond);
    private readonly Lock _gate = new();

    private TelemetryRoute(ITelemetryRelay? relay)
    {
        _relay = relay;
    }

    /// <summary>Whether the layer is in the app and an endpoint is set.</summary>
    public bool Enabled => _relay is { Enabled: true };

    /// <summary>The route for an app: the layer's relay when <c>Program.cs</c> called <c>spar.Telemetry(…)</c>, else every request is a 404.</summary>
    public static TelemetryRoute For(SparBuilder builder) => new(builder.TelemetryRelay);

    /// <summary>A route without the layer, for a host that has no services yet: every request is a 404.</summary>
    public static TelemetryRoute None { get; } = new(null);

    public static bool Matches(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The answer that needs no body (404, 405, 415, 429, or the configuration), or null when the request is a traces
    /// request to read and forward. Called on the thread that answers requests.
    /// </summary>
    public PageResponse? Check(string path, string method, string? contentType)
    {
        if (_relay is not { Enabled: true } && path == ConfigPath && method == "POST")
        {
            return Plain(204, "No Content");
        }

        if (_relay is not { Enabled: true } || path is not (TracesPath or ConfigPath))
        {
            return NotFound();
        }

        if (method != "POST")
        {
            return new PageResponse(405, "Method Not Allowed", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty, Allow: "POST");
        }

        if (path == ConfigPath)
        {
            return Config(_relay);
        }

        if (!IsOtlp(contentType))
        {
            return Plain(415, "Unsupported Media Type");
        }

        // Loopback answers requests on several threads; the desktop hosts on one.
        lock (_gate)
        {
            return _limiter.TryTake(Stopwatch.GetTimestamp()) ? null : Plain(429, "Too Many Requests");
        }
    }

    /// <summary>Forwards a traces request's body, read by the host (at most <see cref="MaxBody"/> + 1 bytes).</summary>
    public async Task<PageResponse> ForwardAsync(ReadOnlyMemory<byte> body, string contentType, CancellationToken cancellationToken)
    {
        if (_relay is not { Enabled: true } relay)
        {
            return NotFound();
        }

        if (body.Length > MaxBody)
        {
            return Plain(413, "Content Too Large");
        }

        var answer = await relay.ForwardTracesAsync(body, contentType, cancellationToken).ConfigureAwait(false);
        return Plain(answer.Status, answer.Reason);
    }

    /// <summary>
    /// The page's settings as a statement of the page's world, for the host-injected script of a desktop host, or null
    /// when telemetry is off (the page then loads none of its code).
    /// </summary>
    public string? PageScript() => _relay is { Enabled: true } relay
        ? $"globalThis.__sparTelemetry = Object.freeze({Settings(relay)});"
        : null;

    private static PageResponse Config(ITelemetryRelay relay) =>
        new(200, "OK", "application/json", Encoding.UTF8.GetBytes(Settings(relay)));

    /// <summary><c>{"serviceName":"…","delayMs":5000}</c>, the page module's settings.</summary>
    private static string Settings(ITelemetryRelay relay)
    {
        var name = System.Text.Json.JsonEncodedText.Encode(relay.PageServiceName).ToString();
        var delay = relay.PageDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        return "{\"serviceName\":\"" + name + "\",\"delayMs\":" + delay + "}";
    }

    private static bool IsOtlp(string? contentType)
    {
        var type = contentType?.Split(';')[0].Trim();
        return string.Equals(type, "application/x-protobuf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static PageResponse NotFound() => Plain(404, "Not Found");

    private static PageResponse Plain(int status, string reason) =>
        new(status, reason, "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty);
}
