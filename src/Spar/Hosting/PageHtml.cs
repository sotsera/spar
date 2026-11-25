using System.Security.Cryptography;
using System.Text;

namespace Spar.Hosting;

/// <summary>
/// What hosts need from the page's HTML for its policy (plan section 3b), read once at start off the UI thread: the
/// hash of the Subresource Integrity import map the Spar Vite plugin writes, which <c>script-src</c> must name since an
/// import map is an inline script; the hashes of the theme style and the splash style the plugin writes (plan section
/// 3d), which <c>style-src</c> names; and where a document's style nonce goes. Only Spar's own inline elements are
/// hashed, each in the exact form the plugin writes (<c>&lt;script type="importmap"&gt;</c>,
/// <c>&lt;style id="spar-theme"&gt;</c>, <c>&lt;style id="spar-splash-style"&gt;</c>), so no other inline script or
/// style is ever allowed.
/// </summary>
internal static class PageHtml
{
    private static readonly byte[] ImportMapStart = "<script type=\"importmap\">"u8.ToArray();
    private static readonly byte[] ScriptEnd = "</script>"u8.ToArray();
    private static readonly byte[] ThemeStyleStart = "<style id=\"spar-theme\">"u8.ToArray();
    private static readonly byte[] SplashStyleStart = "<style id=\"spar-splash-style\">"u8.ToArray();
    private static readonly byte[] StyleEnd = "</style>"u8.ToArray();
    private static readonly byte[] Head = "<head"u8.ToArray();

    /// <summary>
    /// The <c>'sha256-…'</c> source of every import map in the frontend's HTML files, each once: the import map's text is
    /// hashed exactly as it sits between its tags, as the browser does.
    /// </summary>
    public static IReadOnlyList<string> ImportMapHashes(SparFrontend frontend) => HashesOf(frontend, ImportMapHashes);

    /// <summary>
    /// The <c>'sha256-…'</c> source of every theme and splash style in the frontend's HTML files, each once, for
    /// <c>style-src</c>: hashed exactly as they sit between their tags.
    /// </summary>
    public static IReadOnlyList<string> StyleHashes(SparFrontend frontend) => HashesOf(frontend, StyleHashes);

    /// <summary>The <c>'sha256-…'</c> source of each import map in one HTML document.</summary>
    public static List<string> ImportMapHashes(ReadOnlySpan<byte> html)
    {
        var hashes = new List<string>();
        Collect(html, ImportMapStart, ScriptEnd, hashes);
        return hashes;
    }

    /// <summary>The <c>'sha256-…'</c> source of the theme style and the splash style in one HTML document.</summary>
    public static List<string> StyleHashes(ReadOnlySpan<byte> html)
    {
        var hashes = new List<string>();
        Collect(html, ThemeStyleStart, StyleEnd, hashes);
        Collect(html, SplashStyleStart, StyleEnd, hashes);
        return hashes;
    }

    private static List<string> HashesOf(SparFrontend frontend, Func<ReadOnlySpan<byte>, List<string>> read)
    {
        var hashes = new List<string>();
        foreach (var entry in frontend.Entries)
        {
            if (entry.ContentType.StartsWith("text/html", StringComparison.Ordinal) && frontend.TryGet(entry.Path, out var html, out _))
            {
                foreach (var hash in read(html.Span))
                {
                    if (!hashes.Contains(hash))
                    {
                        hashes.Add(hash);
                    }
                }
            }
        }

        return hashes;
    }

    /// <summary>Adds the hash of the text between each <paramref name="start"/> tag and the next <paramref name="end"/>.</summary>
    private static void Collect(ReadOnlySpan<byte> html, ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, List<string> hashes)
    {
        var rest = html;
        while (true)
        {
            var at = rest.IndexOf(start);
            if (at < 0)
            {
                return;
            }

            rest = rest[(at + start.Length)..];
            var close = rest.IndexOf(end);
            if (close < 0)
            {
                return;
            }

            var hash = $"'sha256-{Convert.ToBase64String(SHA256.HashData(rest[..close]))}'";
            if (!hashes.Contains(hash))
            {
                hashes.Add(hash);
            }

            rest = rest[close..];
        }
    }

    /// <summary>
    /// Where a document's <c>&lt;meta property="csp-nonce"&gt;</c> goes: just after the <c>&lt;head&gt;</c> tag, so it
    /// is there before any script reads it; at the start when the document has no <c>&lt;head&gt;</c> tag.
    /// </summary>
    public static int NonceInsertionPoint(ReadOnlySpan<byte> html)
    {
        var at = 0;
        while (true)
        {
            var found = IndexOfIgnoringCase(html[at..], Head);
            if (found < 0)
            {
                return 0;
            }

            var tag = at + found;
            var after = tag + Head.Length;
            if (after < html.Length && (html[after] == '>' || html[after] == ' ' || html[after] == '\t' || html[after] == '\n' || html[after] == '\r'))
            {
                var close = html[after..].IndexOf((byte)'>');
                return close < 0 ? 0 : after + close + 1;
            }

            // <header> and other names that start with "head".
            at = after;
        }
    }

    /// <summary>The meta element that hands the page its style nonce; read it as <c>meta.nonce</c>.</summary>
    public static byte[] NonceMeta(string nonce) => Encoding.UTF8.GetBytes($"<meta property=\"csp-nonce\" nonce=\"{nonce}\">");

    /// <summary>A fresh nonce: 18 random bytes in base64, which needs no escaping in HTML or the header.</summary>
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    private static int IndexOfIgnoringCase(ReadOnlySpan<byte> text, ReadOnlySpan<byte> lowerCase)
    {
        for (var i = 0; i + lowerCase.Length <= text.Length; i++)
        {
            var match = true;
            for (var j = 0; j < lowerCase.Length; j++)
            {
                var character = text[i + j];
                if (character is >= (byte)'A' and <= (byte)'Z')
                {
                    character += 32;
                }

                if (character != lowerCase[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
