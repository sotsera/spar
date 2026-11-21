namespace Spar.Hosting;

/// <summary>
/// What the navigation lock of plan section 3b lets through, the same on every desktop host: the address a user's
/// click may open outside the app (decision "Opening links and files outside the app"), and the frames an app declared
/// with <c>csp.FrameSrc(…)</c>. The hosts call these from their WebView's events and answer from what they return.
/// </summary>
internal static class NavigationRules
{
    /// <summary>The longest address handed to the system: longer ones are refused, not cut.</summary>
    public const int MaxExternalLength = 2_048;

    /// <summary>
    /// Why <paramref name="address"/> may not be opened outside the app, or null when it may: only <c>http</c>,
    /// <c>https</c> (with a host and no credentials, not the app's own <paramref name="origin"/>), <c>mailto</c> and
    /// <c>tel</c>, at most <see cref="MaxExternalLength"/> characters, without spaces, quotes or control characters.
    /// That it came from a user's action is the caller's to check.
    /// </summary>
    public static string? ExternalRefusal(string address, string origin)
    {
        if (address.Length == 0 || address.Length > MaxExternalLength)
        {
            return $"the address is empty or longer than {MaxExternalLength} characters";
        }

        foreach (var character in address)
        {
            if (character <= ' ' || character == '"' || character == '\x7f')
            {
                return "the address has spaces, quotes or control characters";
            }
        }

        var colon = address.IndexOf(':', StringComparison.Ordinal);
        var scheme = colon <= 0 ? "" : address[..colon].ToLowerInvariant();
        switch (scheme)
        {
            case "http" or "https" when PageResponder.IsOnOrigin(address, origin):
                return "the app's own pages do not open in new windows";
            case "http" or "https":
                return HttpRefusal(address, colon);
            case "mailto" or "tel":
                return colon + 1 < address.Length ? null : "the address is empty";
            default:
                return $"only http, https, mailto and tel open outside the app, not '{(scheme.Length == 0 ? "?" : scheme)}:'";
        }
    }

    /// <summary>
    /// Whether a frame may load <paramref name="address"/> because the app declared it in <c>csp.FrameSrc(…)</c>
    /// (<paramref name="sources"/>, as the build normalized them): <c>'self'</c> is the app's own
    /// <paramref name="origin"/>; a scheme alone (<c>https:</c>) matches that scheme; a host source matches its scheme
    /// (<c>http</c> and <c>https</c> when it names none), its host (<c>*.</c> for any subdomain) and its port (<c>*</c>
    /// for any, the scheme's default when it names none). Paths are not compared: the page's own <c>frame-src</c>
    /// enforces them, and this check only keeps the lock from letting through more than the app declared.
    /// </summary>
    public static bool IsDeclaredFrame(string address, IReadOnlyList<string> sources, string origin)
    {
        if (!TrySplit(address, out var scheme, out var host, out var port))
        {
            return false;
        }

        foreach (var source in sources)
        {
            if (source == "'self'")
            {
                if (PageResponder.IsOnOrigin(address, origin))
                {
                    return true;
                }

                continue;
            }

            if (SourceMatches(source, scheme, host, port))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>scheme://host…</c> with a host and no <c>user:password@</c> before it.</summary>
    private static string? HttpRefusal(string address, int colon)
    {
        if (string.CompareOrdinal(address, colon, "://", 0, 3) != 0)
        {
            return "the address has no host";
        }

        var authorityStart = colon + 3;
        var authorityEnd = address.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? address[authorityStart..] : address[authorityStart..authorityEnd];
        if (authority.Length == 0)
        {
            return "the address has no host";
        }

        return authority.Contains('@', StringComparison.Ordinal) ? "the address carries credentials" : null;
    }

    private static bool SourceMatches(string source, string scheme, string host, int port)
    {
        // A scheme alone: "https:", "data:".
        if (source.EndsWith(':') && source.IndexOf('/', StringComparison.Ordinal) < 0)
        {
            return string.Equals(source[..^1], scheme, StringComparison.OrdinalIgnoreCase);
        }

        var rest = source;
        string? sourceScheme = null;
        var separator = rest.IndexOf("://", StringComparison.Ordinal);
        if (separator > 0)
        {
            sourceScheme = rest[..separator];
            rest = rest[(separator + 3)..];
        }

        if (sourceScheme is null ? scheme is not ("http" or "https") : !string.Equals(sourceScheme, scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pathStart = rest.IndexOf('/', StringComparison.Ordinal);
        if (pathStart >= 0)
        {
            rest = rest[..pathStart];
        }

        var sourcePort = DefaultPort(scheme);
        var portStart = rest.LastIndexOf(':');
        if (portStart >= 0 && !rest.EndsWith(']'))
        {
            var text = rest[(portStart + 1)..];
            rest = rest[..portStart];
            if (text == "*")
            {
                sourcePort = -1;
            }
            else if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out sourcePort))
            {
                return false;
            }
        }

        if (sourcePort != -1 && sourcePort != port)
        {
            return false;
        }

        if (rest == "*")
        {
            return true;
        }

        if (rest.StartsWith("*.", StringComparison.Ordinal))
        {
            return host.EndsWith(rest[1..], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(rest, host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An address's scheme, host and port (the scheme's default when it names none); false without a host.</summary>
    private static bool TrySplit(string address, out string scheme, out string host, out int port)
    {
        scheme = "";
        host = "";
        port = -1;
        var separator = address.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        scheme = address[..separator].ToLowerInvariant();
        var authorityStart = separator + 3;
        var authorityEnd = address.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? address[authorityStart..] : address[authorityStart..authorityEnd];
        if (authority.Length == 0 || authority.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        port = DefaultPort(scheme);
        var portStart = authority.LastIndexOf(':');
        if (portStart >= 0 && !authority.EndsWith(']'))
        {
            if (!int.TryParse(authority.AsSpan(portStart + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port))
            {
                return false;
            }

            authority = authority[..portStart];
        }

        host = authority;
        return host.Length > 0;
    }

    private static int DefaultPort(string scheme) => scheme.ToLowerInvariant() switch
    {
        "http" or "ws" => 80,
        "https" or "wss" => 443,
        _ => -1,
    };
}
