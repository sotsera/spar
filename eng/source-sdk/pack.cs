// Packs Spar.Sdk 0.0.0-source, the package that lets this repository's projects write <Project Sdk="Spar.Sdk">
// (eng/source-sdk/Sdk/Sdk.props says how it works). Run it after changing Spar.Sdk.nuspec or the files in Sdk/:
//   dotnet run eng/source-sdk/pack.cs
// It writes eng/source-sdk/feed/spar.sdk.0.0.0-source.nupkg only when its content changes, with fixed timestamps and
// LF line endings, so the same sources give the same bytes. NuGet keeps an extracted copy per version in its package
// folder, so a changed package also needs a new version, here, in the nuspec and in global.json; a test in
// Spar.Sdk.Tasks.Tests checks that the three agree and that the package holds the current sources.
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

var folder = Path.GetDirectoryName(ThisFile())!;
var output = Path.Combine(folder, "feed", "spar.sdk.0.0.0-source.nupkg");
var entries = new (string Name, string Text)[]
{
    ("[Content_Types].xml", ContentTypes),
    ("Spar.Sdk.nuspec", Read("Spar.Sdk.nuspec")),
    ("Sdk/Sdk.props", Read("Sdk/Sdk.props")),
    ("Sdk/Sdk.targets", Read("Sdk/Sdk.targets")),
};

var bytes = Pack(entries);
if (File.Exists(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(bytes))
{
    Console.WriteLine($"Unchanged: {output}");
    return 0;
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllBytes(output, bytes);
Console.WriteLine($"Written: {output}");
return 0;

string Read(string relative) => File.ReadAllText(Path.Combine(folder, relative)).ReplaceLineEndings("\n");

static byte[] Pack((string Name, string Text)[] entries)
{
    var stamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var (name, text) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = stamp;
            using var writer = entry.Open();
            writer.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
        }
    }

    return stream.ToArray();
}

static string ThisFile([CallerFilePath] string path = "") => path;

partial class Program
{
    private const string ContentTypes = """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="nuspec" ContentType="application/octet" />
          <Default Extension="props" ContentType="application/octet" />
          <Default Extension="targets" ContentType="application/octet" />
        </Types>

        """;
}
