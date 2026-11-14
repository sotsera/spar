using System.IO.Compression;
using System.Text;
using Esproj.App;
using Spar.Generated;

var notes = new NotesService(TimeProvider.System, new InMemoryNoteStore());
object Resolve(Type type) => type == typeof(NotesService) ? notes : throw new InvalidOperationException(type.Name);

var saved = await SparCatalog.InvokeAsync(Resolve, "notes", "save", """{"text":"hello","tags":["a"]}"""u8.ToArray(), CancellationToken.None);
var count = await SparCatalog.InvokeAsync(Resolve, "notes", "count", default, CancellationToken.None);
Console.WriteLine($"save: {Encoding.UTF8.GetString(saved)}");
Console.WriteLine($"count: {Encoding.UTF8.GetString(count)}");

// The frontend comes from the .esproj's build output, packed into one embedded zip.
using var resource = typeof(NotesService).Assembly.GetManifestResourceStream("Spar.Frontend")
    ?? throw new InvalidOperationException("Frontend resource missing.");
using var zip = new ZipArchive(resource);
foreach (var entry in zip.Entries)
{
    Console.WriteLine($"frontend: {entry.FullName} {entry.Length}");
}
