namespace Spar.Hosting;

/// <summary>
/// The exact <c>Content-Type</c> of each file type a page may load (plan section 3b). The packing step links this file
/// too, so a packed page carries the same types the hosts send. Under <c>nosniff</c> a wrong type blocks the file,
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
