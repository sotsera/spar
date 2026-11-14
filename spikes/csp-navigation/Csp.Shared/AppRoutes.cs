using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Csp.Shared;

/// <summary>A complete response from the app origin, the same on every host.</summary>
public sealed record HostResponse(int Status, string Reason, string ContentType, byte[] Body, List<KeyValuePair<string, string>> Headers)
{
    public static HostResponse Text(int status, string reason, string text) =>
        new(status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), []);
}

public static class Log
{
    private static readonly Stopwatch s_clock = Stopwatch.StartNew();

    public static double Now => s_clock.Elapsed.TotalMilliseconds;

    public static void Write(string line) =>
        Console.WriteLine($"[{s_clock.Elapsed.TotalMilliseconds,9:F1}] {line}");
}

/// <summary>
/// The app origin's routes, shared by the WebView2 host (WebResourceRequested), the WebKitGTK host (spar:// scheme)
/// and the loopback server: files from the served roots with the profile's headers, a few test routes, and
/// <c>POST /_spar/log</c> for the page's results.
/// </summary>
public sealed class AppRoutes
{
    private readonly string[] _roots;
    private readonly HeaderProfile _profile;
    private readonly string? _tamper;
    private readonly Action<JsonElement> _onPageMessage;

    public AppRoutes(string[] roots, HeaderProfile profile, string? tamper, Action<JsonElement> onPageMessage)
    {
        _roots = roots;
        _profile = profile;
        _tamper = tamper;
        _onPageMessage = onPageMessage;
    }

    public HostResponse Handle(string method, string pathAndQuery, byte[] body, Func<string, string?> header)
    {
        var queryStart = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        var path = Uri.UnescapeDataString(queryStart < 0 ? pathAndQuery : pathAndQuery[..queryStart]);
        var query = queryStart < 0 ? "" : pathAndQuery[(queryStart + 1)..];
        var response = Route(method, path, query, body, header);
        response.Headers.Add(new("Cache-Control", "no-store"));
        if (_profile.Csp is { } csp)
        {
            // On every response: dedicated workers take their policy from their own script response.
            response.Headers.Add(new("Content-Security-Policy", csp));
        }

        response.Headers.AddRange(_profile.Headers);
        return response;
    }

    private HostResponse Route(string method, string path, string query, byte[] body, Func<string, string?> header)
    {
        if (path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            var text = Encoding.UTF8.GetString(body);
            switch (path)
            {
                case "/_spar/log" when method == "POST":
                    Log.Write("PAGE " + text);
                    try
                    {
                        using var document = JsonDocument.Parse(body);
                        _onPageMessage(document.RootElement.Clone());
                    }
                    catch (JsonException exception)
                    {
                        Log.Write("PAGE_BAD_JSON " + exception.Message);
                    }

                    return HostResponse.Text(200, "OK", "ok");
                case "/_spar/report" or "/_spar/report-to":
                    Log.Write($"REPORT {path} method={method} type={header("Content-Type") ?? "-"} {text}");
                    return HostResponse.Text(204, "No Content", "");
                case "/_spar/form":
                    Log.Write($"FORM_SELF method={method} {text}");
                    return new HostResponse(200, "OK", "text/html; charset=utf-8", "<!doctype html><p>form received</p>"u8.ToArray(), []);
                default:
                    return HostResponse.Text(404, "Not Found", "unknown route");
            }
        }

        if (path == "/_test/wrong-type.js")
        {
            // A script with a non-script type: only X-Content-Type-Options: nosniff should stop it.
            return new HostResponse(200, "OK", "text/plain", "window.__wrongType = true;"u8.ToArray(), []);
        }

        if (path == "/_test/redirect")
        {
            var target = Uri.UnescapeDataString(query.StartsWith("to=", StringComparison.Ordinal) ? query[3..] : "");
            Log.Write("REDIRECT_SERVED to=" + target);
            var redirect = HostResponse.Text(302, "Found", "redirect");
            redirect.Headers.Add(new("Location", target));
            return redirect;
        }

        var file = Find(path == "/" ? "index.html" : path.TrimStart('/'));
        if (file is null)
        {
            return HostResponse.Text(404, "Not Found", "not found: " + path);
        }

        var bytes = File.ReadAllBytes(file);
        var type = ContentType(file);
        if (_tamper is not null && path.Contains(_tamper, StringComparison.Ordinal) && type.StartsWith("text/", StringComparison.Ordinal))
        {
            bytes = Tamper(bytes);
            Log.Write($"TAMPERED {path} ({bytes.Length} B, one byte changed)");
        }

        if (_profile.Meta is { } meta && type.StartsWith("text/html", StringComparison.Ordinal))
        {
            bytes = InsertMeta(bytes, meta);
        }

        return new HostResponse(200, "OK", type, bytes, []);
    }

    private string? Find(string relative)
    {
        foreach (var root in _roots)
        {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal) && File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    /// <summary>Changes one byte without changing what the code does: the last newline becomes a space, or a space is appended.</summary>
    private static byte[] Tamper(byte[] bytes)
    {
        var copy = (byte[])bytes.Clone();
        if (copy.Length > 0 && copy[^1] == (byte)'\n')
        {
            copy[^1] = (byte)' ';
            return copy;
        }

        return [.. copy, (byte)' '];
    }

    private static byte[] InsertMeta(byte[] html, string policy)
    {
        var text = Encoding.UTF8.GetString(html);
        var head = text.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        var tag = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{policy.Replace("\"", "&quot;", StringComparison.Ordinal)}\">";
        text = head < 0 ? tag + text : text.Insert(head + "<head>".Length, tag);
        return Encoding.UTF8.GetBytes(text);
    }

    public static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream",
    };

    public static string Ms(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
