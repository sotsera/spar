#if SPAR_ENCODING_PROBE
using System.IO.Compression;
using System.Text;

namespace Spar.AssetPacking;

/// <summary>
/// Probe build only (-p:PackProbe=true): does the web view decode a <c>Content-Encoding</c> on responses that the host
/// answers itself? <c>/probe.html</c> fetches <c>/probe/{br,gzip,identity}.txt</c> and loads
/// <c>/probe-script/{br,gzip}.js</c>, then reports what it saw in the path of <c>POST /_spar/probe/…</c>.
/// </summary>
internal static class EncodingProbe
{
    public static readonly byte[] Html = Encoding.UTF8.GetBytes("""
        <!doctype html><meta charset="utf-8"><title>probe</title>
        <script type="module">
        const results = [];
        for (const encoding of ["br", "gzip", "identity"]) {
          try {
            const response = await fetch(`/probe/${encoding}.txt`);
            const text = await response.text();
            results.push(`fetch-${encoding}=${response.status},ce:${response.headers.get("content-encoding")},len:${text.length},ok:${text.startsWith("PROBE-OK")}`);
          } catch (error) {
            results.push(`fetch-${encoding}=error:${error}`);
          }
        }
        for (const encoding of ["br", "gzip"]) {
          await new Promise(resolve => {
            const script = document.createElement("script");
            script.src = `/probe-script/${encoding}.js`;
            script.onload = () => resolve();
            script.onerror = () => resolve();
            document.head.append(script);
          });
          results.push(`script-${encoding}=${globalThis["probe_" + encoding] === true}`);
        }
        await fetch("/_spar/probe/" + encodeURIComponent(results.join(" ")), { method: "POST" });
        await fetch("/_spar/ready", { method: "POST" });
        </script>
        """);

    public static readonly string Text = "PROBE-OK " + string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog ", 50));

    /// <summary>Answers a probe path: (content, Content-Encoding or null), or null when the path is not a probe.</summary>
    public static (byte[] Content, string? Encoding, string ContentType)? Answer(string path)
    {
        if (path == "/probe.html")
        {
            return (Html, null, "text/html; charset=utf-8");
        }

        foreach (var (prefix, type) in new[] { ("/probe/", "text/plain; charset=utf-8"), ("/probe-script/", "text/javascript; charset=utf-8") })
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var encoding = Path.GetFileNameWithoutExtension(path);
            var plain = Encoding.UTF8.GetBytes(prefix == "/probe/" ? Text : $"globalThis.probe_{encoding} = true;");
            return encoding switch
            {
                "br" => (Brotli(plain), "br", type),
                "gzip" => (Gzip(plain), "gzip", type),
                _ => (plain, null, type),
            };
        }

        return null;
    }

    private static byte[] Brotli(byte[] plain)
    {
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(plain.Length)];
        BrotliEncoder.TryCompress(plain, buffer, out var written, 11, 22);
        return buffer[..written];
    }

    private static byte[] Gzip(byte[] plain)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(plain);
        }

        return output.ToArray();
    }
}
#endif
