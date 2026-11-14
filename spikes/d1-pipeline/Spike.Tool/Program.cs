using System.Diagnostics;
using Spike.Tool;

// spike-tool client <inventory> <outDir>   writes contracts.json and one TypeScript module per contract
// spike-tool pack <zip> <dir> [<dir>...]   writes a deterministic zip of the directories' files
var stopwatch = Stopwatch.StartNew();
switch (args)
{
    case ["client", var inventory, var outDir]:
        var (contracts, sources, references, reused) = ClientBuilder.GenerateClient(Path.GetFullPath(inventory), Path.GetFullPath(outDir));
        Console.Error.WriteLine($"spike-tool client: {contracts} contract(s), {sources} source(s), {references} reference(s), {reused} reused");
        break;
    case ["pack", var zip, .. var dirs] when dirs.Length > 0:
        ClientBuilder.Pack(Path.GetFullPath(zip), dirs.Select(Path.GetFullPath));
        break;
    default:
        Console.Error.WriteLine("usage: spike-tool client <inventory> <outDir> | pack <zip> <dir>...");
        return 2;
}

Console.Error.WriteLine($"spike-tool {args[0]}: {stopwatch.ElapsedMilliseconds} ms");
return 0;
