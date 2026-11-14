using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Spar.Generated;
using Spike;
using SparApp;

var host = args is ["--host", var value] ? value : "desktop";
Console.WriteLine($"SparApp host={host} runtime={SparRuntimeInfo.Version} generator={SparCatalog.GeneratorVersion}");

var service = new NotesService();
object Resolve(Type type) => type == typeof(NotesService) ? service : throw new InvalidOperationException(type.Name);

var save = """{"text":"hello","tags":["a"],"score":1.5,"pinned":true}"""u8.ToArray();
var saved = await SparCatalog.InvokeAsync(Resolve, "notes", "save", save, CancellationToken.None);
Console.WriteLine("notes.save -> " + Encoding.UTF8.GetString(saved));
var count = await SparCatalog.InvokeAsync(Resolve, "notes", "count", default, CancellationToken.None);
Console.WriteLine("notes.count -> " + Encoding.UTF8.GetString(count));

using var resource = typeof(NotesService).Assembly.GetManifestResourceStream("Spar.Frontend")
    ?? throw new InvalidOperationException("Frontend resource missing.");
using var zip = new ZipArchive(resource);
Console.WriteLine("frontend: " + string.Join(", ", zip.Entries.Select(e => e.FullName)));

if (host == "loopback")
{
    var entry = zip.GetEntry("index.html") ?? throw new InvalidOperationException("index.html missing.");
    using var page = new MemoryStream();
    using (var entryStream = entry.Open())
    {
        entryStream.CopyTo(page);
    }

    var body = await Loopback.ServeOnceAsync(page.ToArray());
    Console.WriteLine($"loopback GET /index.html -> {body.Length} chars, starts with {body[..15]}");
}

/// <summary>Serves one page on 127.0.0.1 and fetches it, as a smoke test of the loopback path.</summary>
internal static class Loopback
{
    public static async Task<string> ServeOnceAsync(byte[] page)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var request = new StringBuilder();
            var buffer = new byte[1024];
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    return;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {page.Length}\r\n"
                + "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(page);
        });

        using var http = new HttpClient();
        var body = await http.GetStringAsync(new Uri($"http://127.0.0.1:{port}/index.html"));
        await serve;
        return body;
    }
}
