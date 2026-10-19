using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Sotsera.Spar.Windows;

namespace Spar.NativeSample;

internal static class SampleDocument
{
    public static SparAssetBundle CreateAssets()
    {
        var html = Read("Spar.Sample.App.html");
        var css = Regex.Match(html, "<style>([\\s\\S]*?)</style>").Groups[1].Value;
        html = Regex.Replace(html, "<meta http-equiv=\"Content-Security-Policy\"[\\s\\S]*?>", "");
        html = Regex.Replace(html, "<style>[\\s\\S]*?</style>", "<link rel=\"stylesheet\" href=\"/app.css\">");
        html = Regex.Replace(html, "<script type=\"module\">[\\s\\S]*?</script>",
            "<script type=\"module\" src=\"/app.js\"></script>");
        var checks = """
            const blocked = source => new Promise((resolve, reject) => {
              const script = document.createElement('script');
              script.src = source;
              script.onload = () => reject(new Error('Unauthorized resource loaded: ' + source));
              script.onerror = resolve;
              document.head.append(script);
            });
            await blocked('/missing.js');
            await blocked('/app.js?alias=1');
            await blocked('https://foreign.invalid/app.js');
            await blocked('/spar-contract.txt');
            """;
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("index.html", html);
            Add("app.css", css);
            Add("app.js", Read("Spar.Generated.Client.js") + "\nconst closeProbe = false;\n" + checks
                + Read("Spar.Sample.Probe.js"));
            Add("spar-contract.txt", new SampleBackendBridge(null!).Fingerprint);

            void Add(string name, string content)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        stream.Position = 0;
        return SparAssetBundle.Read(stream);
    }

    public static string Create(bool probe, bool close)
    {
        var client = Read("Spar.Generated.Client.js");
        var application = Read(probe ? "Spar.Sample.Probe.js" : "Spar.Sample.App.js");
        return Read("Spar.Sample.App.html").Replace("/*__CLIENT__*/", client, StringComparison.Ordinal)
            .Replace("/*__APPLICATION__*/", "const closeProbe = " + (close ? "true;\n" : "false;\n") + application,
                StringComparison.Ordinal);
    }

    private static string Read(string name)
    {
        var assembly = name == "Spar.Generated.Client.js" ? typeof(SampleBackendBridge).Assembly : typeof(SampleDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing sample resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
