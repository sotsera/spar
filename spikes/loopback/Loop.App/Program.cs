using Loop.App;
using Spar.Generated;

var notes = new NotesService();
object Resolve(Type type) => type == typeof(NotesService) ? notes : throw new InvalidOperationException(type.Name);

#if SPAR_LOOPBACK
if (args.Contains("--loopback"))
{
    return await Loop.Loopback.LoopbackHost.RunAsync(
        new(
            (contract, command, payload, ct) => SparCatalog.InvokeAsync(Resolve, contract, command, payload, ct),
            SparCatalog.Manifest,
            () => typeof(NotesService).Assembly.GetManifestResourceStream("Spar.Frontend")!),
        args);
}
#endif

// The desktop (WebView2) host is not part of this spike; exercise dispatch so the published size is representative.
var listed = await SparCatalog.InvokeAsync(Resolve, "notes", "list", default, CancellationToken.None);
Console.WriteLine($"desktop host not in this spike; notes.list -> {System.Text.Encoding.UTF8.GetString(listed)}");
return 0;
