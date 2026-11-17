// This file is compiled into Spar and, linked, into Spar.Generator (netstandard2.0), so the build's check of a source
// (SPAR104) and the run-time check in CspBuilder are one rule. Keep it to APIs both targets have.
namespace Spar.Hosting;

/// <summary>The directives an app may widen from C#, one per <c>CspBuilder</c> method.</summary>
internal enum CspDirective
{
    Font,
    Image,
    Connect,
    Media,
    Script,
    Style,
    Frame,
}

/// <summary>A source as the rule read it: its normalized form, or why it is refused.</summary>
internal readonly struct CspSourceCheck
{
    private CspSourceCheck(string? normalized, string? problem)
    {
        Normalized = normalized;
        Problem = problem;
    }

    /// <summary>The source with its scheme and host in lower case; null when refused.</summary>
    public string? Normalized { get; }

    /// <summary>Why the source is refused, naming the method to use instead where there is one; null when accepted.</summary>
    public string? Problem { get; }

    public static CspSourceCheck Accepted(string normalized) => new(normalized, null);

    public static CspSourceCheck Refused(string problem) => new(null, problem);
}

/// <summary>
/// What a directive method of <c>CspBuilder</c> accepts (plan section 3b; decision "Page security's public shape"):
/// hosts (<c>https://fonts.gstatic.com</c>, <c>*.example.com</c>, <c>example.com:8443/path/</c>) and schemes
/// (<c>https:</c>, <c>data:</c>), and <c>'self'</c> in <c>FrameSrc</c> only. Keywords, nonces and hashes are refused
/// with the named method that gives what they would, so a risky widening never hides inside a string.
/// </summary>
internal static class CspSourceRule
{
    /// <summary>The longest source accepted; a real host source is far shorter.</summary>
    public const int MaxLength = 512;

    /// <summary>The C# method name of a directive, for messages and the analyzer.</summary>
    public static string MethodName(CspDirective directive) => directive switch
    {
        CspDirective.Font => "FontSrc",
        CspDirective.Image => "ImgSrc",
        CspDirective.Connect => "ConnectSrc",
        CspDirective.Media => "MediaSrc",
        CspDirective.Script => "ScriptSrc",
        CspDirective.Style => "StyleSrc",
        _ => "FrameSrc",
    };

    public static CspSourceCheck Check(string? source, CspDirective directive)
    {
        var method = MethodName(directive);
        if (source is null || source.Length == 0)
        {
            return CspSourceCheck.Refused($"{method} takes hosts and schemes; an empty source is not one.");
        }

        if (source.Length > MaxLength)
        {
            return CspSourceCheck.Refused($"{method}: a source is at most {MaxLength} characters.");
        }

        if (source[0] == '\'')
        {
            return Keyword(source, directive, method);
        }

        foreach (var character in source)
        {
            if (character <= ' ' || character == ';' || character == ',' || character == '\'' || character == '"' || character > '~')
            {
                return CspSourceCheck.Refused(
                    $"{method}: '{source}' is not one source. Pass each host or scheme as its own argument, without spaces, quotes, ';' or ','.");
            }
        }

        if (source == "*")
        {
            return CspSourceCheck.Refused($"{method}: '*' allows every host. Name the hosts the page needs.");
        }

        return IsScheme(source) ? Scheme(source, directive, method) : Host(source, method);
    }

    private static CspSourceCheck Keyword(string source, CspDirective directive, string method)
    {
        var keyword = source.ToLowerInvariant();
        if (keyword == "'self'")
        {
            return directive == CspDirective.Frame
                ? CspSourceCheck.Accepted("'self'")
                : CspSourceCheck.Refused($"{method}: the app's own origin is already in every directive Spar sends; remove '{source}'.");
        }

        if (keyword == "'unsafe-inline'")
        {
            return CspSourceCheck.Refused(directive == CspDirective.Style
                ? $"{method}: use AllowInlineStyles() instead of '{source}'; its documentation explains the risk."
                : $"{method}: '{source}' is not supported here. Inline scripts are blocked; move the code into the app's bundle.");
        }

        if (keyword == "'unsafe-eval'")
        {
            return CspSourceCheck.Refused($"{method}: use AllowEval() instead of '{source}'; its documentation explains the risk.");
        }

        if (keyword == "'wasm-unsafe-eval'")
        {
            return CspSourceCheck.Refused($"{method}: use AllowWebAssembly() instead of '{source}'; its documentation explains the risk.");
        }

        if (keyword.StartsWith("'nonce-", System.StringComparison.Ordinal))
        {
            return CspSourceCheck.Refused(directive == CspDirective.Style
                ? $"{method}: use StyleNonce() instead of a fixed nonce; Spar gives each document a fresh one."
                : $"{method}: a fixed nonce protects nothing. Spar adds the nonces it needs itself.");
        }

        if (keyword.StartsWith("'sha256-", System.StringComparison.Ordinal)
            || keyword.StartsWith("'sha384-", System.StringComparison.Ordinal)
            || keyword.StartsWith("'sha512-", System.StringComparison.Ordinal))
        {
            return CspSourceCheck.Refused(
                $"{method}: Spar adds the hashes its build needs itself; an external file's hash goes in its integrity attribute.");
        }

        if (keyword == "'none'")
        {
            return CspSourceCheck.Refused($"{method}: '{source}' would block everything; leave the call out, the default is strict.");
        }

        return CspSourceCheck.Refused($"{method}: '{source}' is a keyword; {method} takes hosts and schemes only.");
    }

    /// <summary>A scheme source: letters, digits, <c>+</c>, <c>.</c> and <c>-</c>, starting with a letter, then <c>:</c>.</summary>
    private static bool IsScheme(string source)
    {
        if (source.Length < 2 || source[source.Length - 1] != ':' || !IsLetter(source[0]))
        {
            return false;
        }

        for (var i = 1; i < source.Length - 1; i++)
        {
            if (!IsSchemeCharacter(source[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static CspSourceCheck Scheme(string source, CspDirective directive, string method)
    {
        var scheme = source.ToLowerInvariant();
        if (scheme == "javascript:")
        {
            return CspSourceCheck.Refused($"{method}: 'javascript:' is inline code, which Spar does not allow.");
        }

        if (directive == CspDirective.Script && (scheme == "data:" || scheme == "blob:"))
        {
            return CspSourceCheck.Refused(
                $"{method}: '{source}' lets the page run script it builds itself, as eval would. Bundle the code instead.");
        }

        return CspSourceCheck.Accepted(scheme);
    }

    /// <summary><c>[scheme://]host[:port][/path]</c>, the host possibly starting with <c>*.</c>.</summary>
    private static CspSourceCheck Host(string source, string method)
    {
        var refused = CspSourceCheck.Refused(
            $"{method}: '{source}' is not a host or scheme. Write it as https://example.com, *.example.com or https:.");
        var rest = source;
        var scheme = "";
        var separator = rest.IndexOf("://", System.StringComparison.Ordinal);
        if (separator >= 0)
        {
            scheme = rest.Substring(0, separator + 1);
            if (!IsScheme(scheme))
            {
                return refused;
            }

            rest = rest.Substring(separator + 3);
        }

        var pathStart = rest.IndexOf('/');
        var path = pathStart < 0 ? "" : rest.Substring(pathStart);
        var authority = pathStart < 0 ? rest : rest.Substring(0, pathStart);
        if (authority.IndexOf('@') >= 0)
        {
            return CspSourceCheck.Refused($"{method}: '{source}' has credentials in it; name the host alone.");
        }

        var portStart = authority.IndexOf(':');
        var host = portStart < 0 ? authority : authority.Substring(0, portStart);
        var port = portStart < 0 ? "" : authority.Substring(portStart);
        if (!IsHost(host) || !IsPort(port))
        {
            return refused;
        }

        var normalized = (scheme.Length == 0 ? "" : scheme.ToLowerInvariant() + "//") + host.ToLowerInvariant() + port + path;
        return CspSourceCheck.Accepted(normalized);
    }

    /// <summary>Dot-separated labels of letters, digits and <c>-</c>, the first possibly <c>*</c>.</summary>
    private static bool IsHost(string host)
    {
        if (host.Length == 0)
        {
            return false;
        }

        var labels = host.Split('.');
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            if (index == 0 && label == "*" && labels.Length > 1)
            {
                continue;
            }

            if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-')
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!IsLetter(character) && !IsDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsPort(string port)
    {
        if (port.Length == 0 || port == ":*")
        {
            return true;
        }

        if (port.Length < 2 || port.Length > 6)
        {
            return false;
        }

        for (var i = 1; i < port.Length; i++)
        {
            if (!IsDigit(port[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLetter(char character) => (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z');

    private static bool IsDigit(char character) => character >= '0' && character <= '9';

    private static bool IsSchemeCharacter(char character) =>
        IsLetter(character) || IsDigit(character) || character == '+' || character == '.' || character == '-';
}
