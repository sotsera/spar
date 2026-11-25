namespace Spar.DevServer;

/// <summary>
/// The development server's address: plain HTTP on a loopback address with its port, and nothing else (decision "M1d
/// step 1's shape"). The host fetches the page from it in-process, and the page opens Vite's HMR WebSocket to it
/// directly, so a Debug build's policy names <see cref="HmrSocket"/> in <c>connect-src</c>. Any other host, a scheme other
/// than <c>http</c>, credentials, a path, a query or a fragment is refused: what the proxy fetches stays on this machine,
/// on the one port the address names.
/// </summary>
/// <param name="Origin">The server's origin, <c>http://127.0.0.1:5173</c>, without a trailing slash.</param>
/// <param name="HmrSocket">The HMR WebSocket's origin, <c>ws://127.0.0.1:5173</c>.</param>
internal sealed record DevServerAddress(string Origin, string HmrSocket)
{
    /// <summary>The address <paramref name="text"/> names, or null with the reason in <paramref name="problem"/>.</summary>
    public static DevServerAddress? Parse(string text, out string? problem)
    {
        problem = null;
        var trimmed = text.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
        {
            problem = $"'{text}' is not an http:// address.";
            return null;
        }

        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
        {
            problem = $"'{text}' must be the server's origin alone, such as http://127.0.0.1:5173, with no path, query or credentials.";
            return null;
        }

        if (!IsLoopbackHost(uri.Host))
        {
            problem = $"'{text}' is not on this machine: the development server must listen on 127.0.0.1, [::1] or localhost.";
            return null;
        }

        var authority = $"{uri.Host}:{uri.Port}";
        return new DevServerAddress($"http://{authority}", $"ws://{authority}");
    }

    /// <summary>
    /// The server's address for one request's path and query, which starts with <c>/</c>. The text is appended to the
    /// origin, never resolved against it, so a path such as <c>//other.example/</c> stays a path on this server.
    /// </summary>
    public Uri Target(string pathAndQuery)
    {
        if (!pathAndQuery.StartsWith('/'))
        {
            throw new ArgumentException("A request's path starts with '/'.", nameof(pathAndQuery));
        }

        var target = new Uri(Origin + pathAndQuery, UriKind.Absolute);
        if (!string.Equals($"{target.Scheme}://{target.Host}:{target.Port}", Origin, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A request's path left the development server.", nameof(pathAndQuery));
        }

        return target;
    }

    /// <summary>127.0.0.0/8, [::1] and localhost; never a name that a resolver could send elsewhere.</summary>
    private static bool IsLoopbackHost(string host) =>
        host == "localhost"
        || host == "[::1]"
        || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
