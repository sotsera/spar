using Microsoft.Extensions.DependencyInjection;
using React.App;
using Spar.Generated;

// The React spike's backend (notes, links, lab), unchanged, hosted by the Linux desktop host.
var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<INoteStore>(new InMemoryNoteStore());
services.AddSingleton<NotesService>();
services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
services.AddSingleton<LinksService>();
services.AddSingleton<LabService>();
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
object Resolve(Type type) => provider.GetRequiredService(type);

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This spike host runs on Linux only.");
    return 1;
}

return Linux.Desktop.LinuxHost.Run(
    new(
        "Spar notes",
        (contract, command, payload, ct) => SparCatalog.InvokeAsync(Resolve, contract, command, payload, ct),
        SparCatalog.Manifest,
        typeof(NotesService).Assembly,
        (contract, command, payload, ct) => SparCatalog.OpenStream(Resolve, contract, command, payload, ct)),
    args);
