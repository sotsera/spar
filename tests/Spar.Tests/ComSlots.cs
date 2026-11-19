using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Spar.Tests;

/// <summary>
/// The slot-order check of hand-declared COM interfaces (pitfalls, "Hand-written COM"): every <c>[GeneratedComInterface]</c>
/// of an assembly is found in a MIDL-generated header by its IID, and each of its methods, in declaration order, is the
/// header's method in that vtable slot, counted from 3 after IUnknown's. A placeholder <c>SlotNNN_Name</c> must sit in slot
/// NNN and name the header's method there. The interfaces are read from the assembly's metadata, so the check sees
/// exactly what the build compiled.
/// </summary>
internal static partial class ComSlots
{
    /// <summary>What differs between <paramref name="assembly"/>'s declarations and <paramref name="headerPath"/>; empty when nothing does.</summary>
    public static List<string> Problems(string headerPath, string assembly, out int declaredCount)
    {
        var header = Header(headerPath);
        var declared = Declared(assembly);
        declaredCount = declared.Count;
        var problems = new List<string>();
        foreach (var com in declared)
        {
            if (!header.TryGetValue(com.Iid, out var vtable))
            {
                problems.Add($"{com.Name}: IID {com.Iid} is not an interface of {Path.GetFileName(headerPath)}");
                continue;
            }

            if (com.Name != vtable.Name)
            {
                problems.Add($"{com.Name}: IID {com.Iid} is {vtable.Name} in {Path.GetFileName(headerPath)}");
            }

            for (var index = 0; index < com.Methods.Count; index++)
            {
                var slot = index + 3;
                var actual = com.Methods[index];
                if (slot >= vtable.Slots.Count)
                {
                    problems.Add($"{com.Name}.{actual}: slot {slot} is past the end of {vtable.Name} ({vtable.Slots.Count} slots)");
                    continue;
                }

                var expected = CSharpName(vtable.Slots[slot]);
                var placeholder = PlaceholderPattern().Match(actual);
                var matches = placeholder.Success
                    ? int.Parse(placeholder.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == slot && placeholder.Groups[2].Value == expected
                    : actual == expected;
                if (!matches)
                {
                    problems.Add($"{com.Name}: slot {slot} is {vtable.Slots[slot]} in the header, declared as {actual}");
                }
            }
        }

        return problems;
    }

    /// <summary>The <c>[GeneratedComInterface]</c> interfaces of an assembly beside the tests, with their IIDs and methods in declaration order.</summary>
    public static List<(string Name, Guid Iid, List<string> Methods)> Declared(string assembly)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, assembly + ".dll"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var declared = new List<(string, Guid, List<string>)>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.Interface) == 0)
            {
                continue;
            }

            var attributes = type.GetCustomAttributes().Select(metadata.GetCustomAttribute).ToList();
            if (!attributes.Any(attribute => AttributeName(metadata, attribute) == "GeneratedComInterfaceAttribute"))
            {
                continue;
            }

            var guid = attributes.Single(attribute => AttributeName(metadata, attribute) == "GuidAttribute");
            var methods = type.GetMethods().Select(method => metadata.GetString(metadata.GetMethodDefinition(method).Name)).ToList();
            declared.Add((metadata.GetString(type.Name), Guid.Parse(StringArgument(metadata, guid)), methods));
        }

        return declared;
    }

    /// <summary><c>get_X</c>, <c>put_X</c>, <c>add_X</c> and <c>remove_X</c> become <c>GetX</c>, <c>PutX</c>, <c>AddX</c> and <c>RemoveX</c>.</summary>
    private static string CSharpName(string header)
    {
        var match = AccessorPattern().Match(header);
        return match.Success ? char.ToUpperInvariant(match.Groups[1].Value[0]) + match.Groups[1].Value[1..] + match.Groups[2].Value : header;
    }

    /// <summary>Every interface in a header, by IID, with its vtable's methods in slot order, IUnknown's included.</summary>
    private static Dictionary<Guid, (string Name, List<string> Slots)> Header(string path)
    {
        var text = File.ReadAllText(path);
        var interfaces = new Dictionary<Guid, (string, List<string>)>();
        foreach (Match declaration in InterfacePattern().Matches(text))
        {
            var name = declaration.Groups[2].Value;
            var start = text.IndexOf($"typedef struct {name}Vtbl", StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            var end = text.IndexOf($"}} {name}Vtbl;", start, StringComparison.Ordinal);
            var slots = SlotPattern().Matches(text[start..end]).Select(slot => slot.Groups[1].Value).ToList();
            interfaces[Guid.Parse(declaration.Groups[1].Value)] = (name, slots);
        }

        return interfaces;
    }

    private static string AttributeName(MetadataReader metadata, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return "";
        }

        var parent = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        return parent.Kind == HandleKind.TypeReference ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name) : "";
    }

    /// <summary>The single string argument of an attribute: the blob's prolog, then a serialized string.</summary>
    private static string StringArgument(MetadataReader metadata, CustomAttribute attribute)
    {
        var blob = metadata.GetBlobReader(attribute.Value);
        Assert.Equal(1, blob.ReadUInt16());
        return blob.ReadSerializedString()!;
    }

    [GeneratedRegex("""MIDL_INTERFACE\("([0-9a-fA-F-]{36})"\)\s+(\w+)\s*:""")]
    private static partial Regex InterfacePattern();

    [GeneratedRegex(@"DECLSPEC_XFGVIRT\(\w+,\s*(\w+)\)")]
    private static partial Regex SlotPattern();

    [GeneratedRegex(@"^Slot(\d{3})_(\w+)$")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex("^(get|put|add|remove)_(.+)$")]
    private static partial Regex AccessorPattern();
}
