using System.IO.Compression;
using System.Text;
using Spar.Generated;
using Spike.App;

var service = new NotesService();
object Resolve(Type type) => type == typeof(NotesService) ? service : throw new InvalidOperationException(type.Name);

var save = """{"text":"hello","tags":["a","b"],"parent":{"text":"root","tags":[],"parent":null,"score":1,"pinned":false,"version":null},"score":2.5,"pinned":true,"version":null}"""u8.ToArray();
Console.WriteLine(Encoding.UTF8.GetString((await SparCatalog.InvokeAsync(Resolve, "notes", "save", save, CancellationToken.None)).AsSpan()));
Console.WriteLine(Encoding.UTF8.GetString((await SparCatalog.InvokeAsync(Resolve, "notes", "list", default, CancellationToken.None)).AsSpan()));
Console.WriteLine(Encoding.UTF8.GetString((await SparCatalog.InvokeAsync(Resolve, "notes", "count", default, CancellationToken.None)).AsSpan()));

try
{
    await SparCatalog.InvokeAsync(Resolve, "notes", "save", """{"tags":[]}"""u8.ToArray(), CancellationToken.None);
    Console.WriteLine("missing property: accepted (unexpected)");
}
catch (System.Text.Json.JsonException exception)
{
    Console.WriteLine($"missing property: rejected ({exception.Message})");
}

using var resource = typeof(NotesService).Assembly.GetManifestResourceStream("Spar.Frontend")
    ?? throw new InvalidOperationException("Frontend resource missing.");
using var zip = new ZipArchive(resource);
Console.WriteLine("frontend: " + string.Join(", ", zip.Entries.Select(e => e.FullName)));
Console.WriteLine("manifest: " + SparCatalog.Manifest);
