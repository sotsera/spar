using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebView2Flags;

/// <summary>
/// Summarizes Chromium net logs (JSON, one event per line). Hosts are found in the log, not assumed: every string in
/// every event's parameters is scanned for a URL or a host:port, and each host is listed with the event types it
/// appeared in. URL requests (URL_REQUEST_START_JOB), host resolutions (HOST_RESOLVER_MANAGER_REQUEST) and connect jobs
/// are listed apart. Query strings are dropped from the output.
/// </summary>
internal static partial class NetLog
{
    public static string Summarize(IEnumerable<string> files)
    {
        var text = new StringBuilder();
        foreach (var file in files)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"# {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes)");
            text.Append(SummarizeOne(file));
            text.AppendLine();
        }

        return text.ToString();
    }

    private static string SummarizeOne(string file)
    {
        var raw = File.ReadAllText(file).TrimEnd();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            // A log the browser did not finish: close the events array and the object.
            doc = JsonDocument.Parse(raw.TrimEnd(',', '\n', '\r', ' ') + "]}", new JsonDocumentOptions { AllowTrailingCommas = true });
        }

        using var _ = doc;
        var types = new Dictionary<int, string>();
        foreach (var p in doc.RootElement.GetProperty("constants").GetProperty("logEventTypes").EnumerateObject())
        {
            types[p.Value.GetInt32()] = p.Name;
        }

        var requests = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var resolutions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var connects = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var hosts = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var events = 0;
        foreach (var e in doc.RootElement.GetProperty("events").EnumerateArray())
        {
            events++;
            var type = types.GetValueOrDefault(e.GetProperty("type").GetInt32(), "?");
            if (!e.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (type == "URL_REQUEST_START_JOB" && parameters.TryGetProperty("url", out var url))
            {
                var method = parameters.TryGetProperty("method", out var m) ? m.GetString() : "?";
                Count(requests, $"{method} {StripQuery(url.GetString()!)}");
            }

            if (type == "HOST_RESOLVER_MANAGER_REQUEST" && parameters.TryGetProperty("host", out var host))
            {
                Count(resolutions, host.ToString());
            }

            if (type is "SOCKET_POOL_CONNECT_JOB_CREATED" or "CONNECT_JOB" or "TRANSPORT_CONNECT_JOB_CONNECT" or "TCP_CONNECT" or "SSL_CONNECT_JOB_CONNECT")
            {
                foreach (var name in new[] { "group_id", "group_name", "address_list", "remote_address" })
                {
                    if (parameters.TryGetProperty(name, out var g))
                    {
                        Count(connects, $"{type} {g}");
                    }
                }
            }

            Scan(parameters, type, hosts);
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"events {events}");
        text.AppendLine("## URL requests (URL_REQUEST_START_JOB)");
        foreach (var (k, v) in requests)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{v,4} {k}");
        }

        text.AppendLine("## host resolutions (HOST_RESOLVER_MANAGER_REQUEST)");
        foreach (var (k, v) in resolutions)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{v,4} {k}");
        }

        text.AppendLine("## connect jobs");
        foreach (var (k, v) in connects)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{v,4} {k}");
        }

        text.AppendLine("## every host named in any event's parameters (event types)");
        foreach (var (k, v) in hosts)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{k}: {string.Join(", ", v)}");
        }

        return text.ToString();
    }

    private static void Scan(JsonElement element, string type, SortedDictionary<string, SortedSet<string>> hosts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    Scan(p.Value, type, hosts);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, type, hosts);
                }

                break;
            case JsonValueKind.String:
                foreach (Match match in HostPattern().Matches(element.GetString()!))
                {
                    var host = match.Groups["host"].Value.ToLowerInvariant();
                    if (!hosts.TryGetValue(host, out var set))
                    {
                        hosts[host] = set = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    set.Add(type);
                }

                break;
            default:
                break;
        }
    }

    private static void Count(SortedDictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;

    private static string StripQuery(string url)
    {
        var q = url.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? url : url[..q] + "?…";
    }

    // A scheme URL's host, or a bare dotted name followed by a port (socket groups such as "ssl/host:443").
    [GeneratedRegex(@"(?:(?:https?|wss?)://(?<host>[A-Za-z0-9.\-\[\]:]+?)(?::\d+)?(?=[/?#""\s]|$))|(?:(?<=^|[/\s])(?<host>[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+):\d+)")]
    private static partial Regex HostPattern();
}
