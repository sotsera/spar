using System.Text;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar;

/// <summary>
/// The two blob routes of the app's origin (decision "M3b's decide-first questions", item 2), host-neutral, as every host
/// serves them: <c>/_spar/blob/&lt;id&gt;</c> reads a handle a result gave the page, and
/// <c>/_spar/resource/&lt;contract&gt;/&lt;command&gt;?r=&lt;request JSON&gt;</c> runs a resource command for this load.
/// GET and HEAD only. A loopback host, whose port any local program can reach, also needs the document's key
/// (<c>k=</c>), which the page's channel registered; the desktop hosts answer only their own WebView.
/// </summary>
internal static class BlobRoutes
{
    public const string BlobPrefix = "/_spar/blob/";

    public const string ResourcePrefix = "/_spar/resource/";

    /// <summary>The longest request a resource address may carry, as JSON.</summary>
    public const int MaxRequestBytes = 4096;

    /// <summary>
    /// A blob's own policy: shown as a document (opened in a window, say), it is sandboxed and loads nothing. A PDF keeps
    /// the page's policy, which the browser's viewer needs.
    /// </summary>
    private const string SandboxPolicy = "sandbox; default-src 'none'";

    /// <summary>Whether <paramref name="path"/> is one of the two routes.</summary>
    public static bool Matches(string path) =>
        path.StartsWith(BlobPrefix, StringComparison.Ordinal) || path.StartsWith(ResourcePrefix, StringComparison.Ordinal);

    /// <summary>
    /// The answer to one request, with the blob's bytes read in full (a file off the caller's thread). A host that
    /// knows no document key passes null for <paramref name="ownerOfKey"/>; a loopback host passes the lookup of the
    /// <c>k=</c> value.
    /// </summary>
    public static async Task<PageResponse> AnswerAsync(
        SparDispatcher dispatcher, string method, string path, string query, Func<string, BlobOwner?>? ownerOfKey,
        CancellationToken cancellationToken)
    {
        if (method != "GET" && method != "HEAD")
        {
            return PageResponder.Refusal(405, "Method Not Allowed", "method-not-allowed") with { Allow = "GET, HEAD" };
        }

        var parameters = Parameters(query);
        BlobOwner? owner = null;
        if (ownerOfKey is not null)
        {
            owner = parameters.TryGetValue("k", out var key) ? ownerOfKey(key) : null;
            if (owner is null)
            {
                return PageResponder.Refusal(401, "Unauthorized", "unauthorized");
            }
        }

        if (path.StartsWith(BlobPrefix, StringComparison.Ordinal))
        {
            var id = path[BlobPrefix.Length..];
            var blob = owner is null ? dispatcher.Blobs.Find(id) : dispatcher.Blobs.Find(id, owner);
            return blob is null
                ? PageResponder.Refusal(404, "Not Found", "not-found")
                : await ServeAsync(blob, method, cancellationToken);
        }

        return await LoadResourceAsync(dispatcher, method, path, parameters, cancellationToken);
    }

    private static async Task<PageResponse> LoadResourceAsync(
        SparDispatcher dispatcher, string method, string path, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var names = path[ResourcePrefix.Length..].Split('/');
        if (names.Length != 2 || names[0].Length == 0 || names[1].Length == 0)
        {
            return PageResponder.Refusal(404, "Not Found", "not-found");
        }

        var request = parameters.TryGetValue("r", out var json) ? Encoding.UTF8.GetBytes(json) : [];
        if (request.Length > MaxRequestBytes)
        {
            return PageResponder.Refusal(414, "URI Too Long", "too-large");
        }

        var (outcome, blob) = await dispatcher.InvokeResourceAsync(names[0], names[1], request, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            return failure.Code switch
            {
                "unknown-command" => PageResponder.Refusal(404, "Not Found", "not-found"),
                "invalid-request" => PageResponder.Refusal(400, "Bad Request", "invalid-request"),
                _ => PageResponder.Refusal(500, "Internal Server Error", failure.Code),
            };
        }

        if (blob is null)
        {
            return PageResponder.Refusal(404, "Not Found", "not-found");
        }

        try
        {
            return await ServeAsync(blob, method, cancellationToken);
        }
        finally
        {
            // Served once: a stream's temporary file goes now, unless a result also gave it out as a handle.
            blob.Served();
        }
    }

    private static async Task<PageResponse> ServeAsync(SparBlob blob, string method, CancellationToken cancellationToken)
    {
        var body = method == "HEAD" ? ReadOnlyMemory<byte>.Empty : await blob.ReadAllAsync(cancellationToken);
        var inline = BlobContentTypes.IsInline(blob.ContentType);
        var pdf = blob.ContentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase);
        return new PageResponse(
            200,
            "OK",
            blob.ContentType,
            body,
            Csp: pdf ? null : SandboxPolicy,
            Disposition: inline ? null : BlobContentTypes.Attachment(blob.Name));
    }

    /// <summary>The query's parameters, decoded; the last of a repeated name wins.</summary>
    private static Dictionary<string, string> Parameters(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? "" : pair[(equals + 1)..];
            parameters[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return parameters;
    }
}
