using System.Globalization;

namespace Spar.OpenTelemetry;

/// <summary>
/// The OTLP settings the layer and its relay read, from OpenTelemetry's standard variables: the endpoint
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, without which nothing is exported), the protocol
/// (<c>OTEL_EXPORTER_OTLP_PROTOCOL</c>: <c>grpc</c>, .NET's default, <c>http/protobuf</c> or <c>http/json</c>), the
/// headers (<c>OTEL_EXPORTER_OTLP_HEADERS</c>, <c>key=value</c> pairs separated by commas, URL-encoded), the service name
/// (<c>OTEL_SERVICE_NAME</c>, else <c>service.name</c> in <c>OTEL_RESOURCE_ATTRIBUTES</c>, else the app id), and the
/// batch delay the page's exporter uses (<c>OTEL_BSP_SCHEDULE_DELAY</c>, else 5000 ms).
/// </summary>
internal sealed record OtlpSettings(Uri? Endpoint, string Protocol, IReadOnlyList<KeyValuePair<string, string>> Headers, string ServiceName, int DelayMilliseconds)
{
    public const string Grpc = "grpc";
    public const string HttpProtobuf = "http/protobuf";
    public const string HttpJson = "http/json";

    public bool Enabled => Endpoint is not null;

    public static OtlpSettings Read(Func<string, string?> variable, string appId)
    {
        var endpoint = variable("OTEL_EXPORTER_OTLP_ENDPOINT") is { Length: > 0 } text && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : null;
        var protocol = variable("OTEL_EXPORTER_OTLP_PROTOCOL")?.Trim().ToLowerInvariant() switch
        {
            HttpProtobuf => HttpProtobuf,
            HttpJson => HttpJson,
            _ => Grpc,
        };
        var delay = int.TryParse(variable("OTEL_BSP_SCHEDULE_DELAY"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds > 0
            ? milliseconds
            : 5000;
        return new OtlpSettings(endpoint, protocol, ParsePairs(variable("OTEL_EXPORTER_OTLP_HEADERS")), ReadServiceName(variable, appId), delay);
    }

    /// <summary><c>key=value,key2=value2</c>, each part URL-decoded; a malformed pair is left out.</summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> ParsePairs(string? text)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var pair in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(pair[..equals].Trim());
            var value = Uri.UnescapeDataString(pair[(equals + 1)..].Trim());
            if (name.Length > 0)
            {
                headers.Add(new(name, value));
            }
        }

        return headers;
    }

    private static string ReadServiceName(Func<string, string?> variable, string appId)
    {
        if (variable("OTEL_SERVICE_NAME") is { Length: > 0 } name)
        {
            return name.Trim();
        }

        foreach (var attribute in ParsePairs(variable("OTEL_RESOURCE_ATTRIBUTES")))
        {
            if (attribute.Key == "service.name" && attribute.Value.Length > 0)
            {
                return attribute.Value;
            }
        }

        return appId;
    }
}
