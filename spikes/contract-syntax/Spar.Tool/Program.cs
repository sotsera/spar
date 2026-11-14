using System.Diagnostics;
using Spar.Tool;

// spar-tool client <inventory> <outDir>   writes contracts.json and the TypeScript packages
var stopwatch = Stopwatch.StartNew();
if (args is not ["client", var inventory, var outDir])
{
    Console.Error.WriteLine("usage: spar-tool client <inventory> <outDir>");
    return 2;
}

var (contracts, files, written) = ClientBuilder.GenerateClient(Path.GetFullPath(inventory), Path.GetFullPath(outDir));
Console.Error.WriteLine($"spar-tool client: {contracts} contract(s), {files} file(s), {written} written, {stopwatch.ElapsedMilliseconds} ms");
return 0;
