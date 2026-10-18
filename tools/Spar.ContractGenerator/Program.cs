using Spar.ContractGenerator;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: Spar.ContractGenerator contract.cs adapter.g.cs client.js client.d.ts");
    return 1;
}

try
{
    var generated = ContractCompiler.Generate(File.ReadAllText(args[0]), Path.GetFullPath(args[0]));
    WriteChanged(args[1], generated.CSharp);
    WriteChanged(args[2], generated.JavaScript);
    WriteChanged(args[3], generated.TypeScript);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void WriteChanged(string path, string content)
{
    content = content.ReplaceLineEndings("\n");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    if (!File.Exists(path) || File.ReadAllText(path) != content)
    {
        File.WriteAllText(path, content);
    }
}
