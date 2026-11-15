// Prints the accessibility and interfaces of every type named Program in an assembly (metadata only, nothing loaded).
// Usage: dotnet run tools/inspect.cs <assembly.dll>
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    if (reader.GetString(type.Name) != "Program")
    {
        continue;
    }

    var visibility = (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public ? "public" : "internal";
    var interfaces = type.GetInterfaceImplementations()
        .Select(i => reader.GetInterfaceImplementation(i).Interface)
        .Select(h => h.Kind == HandleKind.TypeReference ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)h).Name) : h.Kind.ToString())
        .ToList();
    var methods = type.GetMethods().Select(m => reader.GetString(reader.GetMethodDefinition(m).Name)).ToList();
    Console.WriteLine($"Program: {visibility}; interfaces [{string.Join(", ", interfaces)}]; methods [{string.Join(", ", methods)}]");
}
