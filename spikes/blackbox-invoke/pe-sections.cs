// The Notebook size question (RESULTS.md, part 2): for each executable given, its file size, the PE file alignment, and
// each section's raw size (on disk, rounded up to the file alignment) and virtual size (the bytes actually used), so a
// size step can be told apart from code growth. Run: dotnet run pe-sections.cs -- <exe> [<exe> ...]
using System.Globalization;
using System.Reflection.PortableExecutable;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

foreach (var path in args)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var headers = pe.PEHeaders;
    var optional = headers.PEHeader!;
    Console.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)}: {stream.Length:N0} B; file alignment {optional.FileAlignment}, section alignment {optional.SectionAlignment}; headers {optional.SizeOfHeaders}");
    var used = 0L;
    foreach (var section in headers.SectionHeaders)
    {
        used += section.VirtualSize;
        Console.WriteLine($"  {section.Name,-8} raw {section.SizeOfRawData,10:N0}  virtual {section.VirtualSize,10:N0}  slack {section.SizeOfRawData - section.VirtualSize,6:N0}");
    }

    var lastEnd = headers.SectionHeaders.Max(s => (long)s.PointerToRawData + s.SizeOfRawData);
    Console.WriteLine($"  sum of virtual sizes {used:N0}; end of last section {lastEnd:N0}; after it {stream.Length - lastEnd:N0}");
    foreach (var entry in pe.ReadDebugDirectory())
    {
        if (entry.Type == DebugDirectoryEntryType.CodeView)
        {
            var codeView = pe.ReadCodeViewDebugDirectoryData(entry);
            Console.WriteLine($"  CodeView PDB path length {codeView.Path.Length} characters");
        }
    }
}
