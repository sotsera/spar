using System.Net;

namespace Sotsera.Spar.Windows;

/// <summary>An explicitly authorized loopback document scope, with a separate client capability.</summary>
/// <remarks>The owner supplies a fresh profile and scope for each session. Never print the URL or credential.</remarks>
public sealed class SparLoopbackContent
{
    public Uri BaseUri
    {
        get;
    }

    internal string ClientCredential
    {
        get;
    }

    public SparLoopbackContent(Uri baseUri, string clientCredential)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(clientCredential);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != "http" || baseUri.UserInfo.Length != 0
            || !IPAddress.TryParse(baseUri.DnsSafeHost, out var address) || !IPAddress.IsLoopback(address)
            || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0
            || baseUri.AbsolutePath == "/" || !baseUri.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("Use an HTTP numeric-loopback URL with a private non-root path ending in '/'.",
                nameof(baseUri));
        }

        if (clientCredential.Length != 64 || !clientCredential.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Use 32 random bytes encoded as 64 hex characters for the client capability.",
                nameof(clientCredential));
        }

        BaseUri = baseUri;
        ClientCredential = clientCredential;
    }

    internal bool AllowsResource(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0
            && uri.Scheme is "http" or "ws" && uri.IdnHost == BaseUri.IdnHost && uri.Port == BaseUri.Port
            && uri.AbsolutePath.StartsWith(BaseUri.AbsolutePath, StringComparison.Ordinal);
    }
}
