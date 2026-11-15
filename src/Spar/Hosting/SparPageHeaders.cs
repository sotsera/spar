namespace Spar.Hosting;

/// <summary>
/// The headers every host puts on every response it serves to the page (plan section 3b): the base Content Security
/// Policy, <c>nosniff</c>, no referrer, the cross-origin policies, the powerful features denied, and no caching. Hosts
/// add an exact <c>Content-Type</c> (<see cref="SparContentTypes"/>). The C# overrides of section 3b arrive in M1b.
/// </summary>
internal static class SparPageHeaders
{
    /// <summary>Every powerful feature denied; an app's declared permissions will open single entries (M1b).</summary>
    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), usb=(), serial=(), hid=(), bluetooth=(), payment=(), display-capture=()";

    /// <summary>Where the page's Content Security Policy sends its violation reports.</summary>
    public const string CspReportPath = "/_spar/csp-report";

    /// <summary>
    /// The base policy. <paramref name="origin"/> is the page's own origin with its scheme, because <c>'self'</c> in
    /// <c>connect-src</c> would also allow WebSockets to the same host, which bypass the host (pitfalls, "Page security").
    /// </summary>
    public static string ContentSecurityPolicy(string origin) =>
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; " +
        $"connect-src {origin}; worker-src 'self'; manifest-src 'self'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; " +
        $"require-trusted-types-for 'script'; report-uri {CspReportPath}";

    /// <summary>The headers for one response, in the order hosts send them.</summary>
    public static KeyValuePair<string, string>[] For(string origin) =>
    [
        new("Content-Security-Policy", ContentSecurityPolicy(origin)),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
        new("Permissions-Policy", PermissionsPolicy),
        new("Cache-Control", "no-store"),
    ];
}

/// <summary>
/// The exact <c>Content-Type</c> of each file type a page may load. Under <c>nosniff</c> a wrong type blocks the file,
/// so a file whose type is not listed is not served at all. Source maps are not listed: they never ship (plan 3c).
/// </summary>
internal static class SparContentTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".webmanifest"] = "application/manifest+json",
        [".txt"] = "text/plain; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".wasm"] = "application/wasm",
    };

    public static bool TryGet(string path, out string contentType)
    {
        if (ByExtension.TryGetValue(Path.GetExtension(path), out var found))
        {
            contentType = found;
            return true;
        }

        contentType = "";
        return false;
    }
}
