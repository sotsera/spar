using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Spar.Tests;

/// <summary>
/// The slot-order test of the Windows host's hand-declared WebView2 interfaces (plan section 3, "Bindings"; pitfalls,
/// "Hand-written COM"): every <c>[GeneratedComInterface]</c> of <c>Spar.Windows</c> is found in <c>WebView2.h</c> by
/// its IID, and each of its methods, in declaration order, is the header's method in that vtable slot, counted from 3
/// after IUnknown's. A placeholder <c>SlotNNN_Name</c> must sit in slot NNN and name the header's method there. The
/// interfaces are read from the assembly's metadata, so the test sees exactly what the build compiled.
/// </summary>
public sealed partial class WebView2SlotTests
{
    /// <summary>The interfaces and slots M1 needs beyond the base set (plan section 3, "Interface slots M1 needs").</summary>
    private static readonly (string Interface, int Slot, string Method)[] Required =
    [
        ("ICoreWebView2_22", 123, "AddWebResourceRequestedFilterWithRequestSourceKinds"),
        ("ICoreWebView2_22", 33, "PostWebMessageAsString"),
        ("ICoreWebView2Environment10", 12, "AddBrowserProcessExited"),
        ("ICoreWebView2Controller", 24, "Close"),
        ("ICoreWebView2WebResourceRequestedEventArgs2", 8, "GetRequestedSourceKind"),
        ("ICoreWebView2Environment10", 20, "CreateCoreWebView2ControllerOptions"),
        ("ICoreWebView2Environment10", 21, "CreateCoreWebView2ControllerWithOptions"),
        ("ICoreWebView2ControllerOptions3", 10, "PutDefaultBackgroundColor"),
        ("ICoreWebView2_22", 105, "GetProfile"),
        ("ICoreWebView2Profile", 9, "PutPreferredColorScheme"),
        ("ICoreWebView2Environment10", 18, "GetProcessInfos"),
        ("ICoreWebView2ProcessInfoCollection", 4, "GetValueAtIndex"),
        ("ICoreWebView2ProcessInfo", 4, "GetKind"),
        ("ICoreWebView2_22", 3, "GetSettings"),
        ("ICoreWebView2_22", 36, "CallDevToolsProtocolMethod"),
        ("ICoreWebView2_22", 75, "AddDownloadStarting"),
        ("ICoreWebView2_22", 100, "AddContextMenuRequested"),
        ("ICoreWebView2Controller", 19, "AddAcceleratorKeyPressed"),
        ("ICoreWebView2Environment10", 15, "GetUserDataFolder"),
        ("ICoreWebView2Settings8", 36, "PutIsReputationCheckingRequired"),
        ("ICoreWebView2NavigationStartingEventArgs3", 12, "GetNavigationKind"),
        ("ICoreWebView2EnvironmentOptions", 3, "GetAdditionalBrowserArguments"),
        ("ICoreWebView2AcceleratorKeyPressedEventArgs2", 10, "PutIsBrowserAcceleratorKeyEnabled"),
        ("ICoreWebView2ContextMenuItemCollection", 5, "RemoveValueAtIndex"),
    ];

    [Fact]
    public void Every_declared_method_sits_in_its_header_slot()
    {
        var header = Header();
        var declared = Declared();
        Assert.NotEmpty(declared);

        var problems = new List<string>();
        foreach (var com in declared)
        {
            if (!header.TryGetValue(com.Iid, out var vtable))
            {
                problems.Add($"{com.Name}: IID {com.Iid} is not an interface of WebView2.h");
                continue;
            }

            if (com.Name != vtable.Name)
            {
                problems.Add($"{com.Name}: IID {com.Iid} is {vtable.Name} in WebView2.h");
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
                    problems.Add($"{com.Name}: slot {slot} is {vtable.Slots[slot]} in WebView2.h, declared as {actual}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_slots_M1_needs_are_declared()
    {
        var declared = Declared().ToDictionary(com => com.Name, StringComparer.Ordinal);

        foreach (var (name, slot, method) in Required)
        {
            Assert.True(declared.TryGetValue(name, out var com), $"{name} is not declared");
            Assert.True(com.Methods.Count > slot - 3 && com.Methods[slot - 3] == method, $"{name} slot {slot} is not {method}");
        }
    }

    /// <summary><c>get_X</c>, <c>put_X</c>, <c>add_X</c> and <c>remove_X</c> become <c>GetX</c>, <c>PutX</c>, <c>AddX</c> and <c>RemoveX</c>.</summary>
    private static string CSharpName(string header)
    {
        var match = AccessorPattern().Match(header);
        return match.Success ? char.ToUpperInvariant(match.Groups[1].Value[0]) + match.Groups[1].Value[1..] + match.Groups[2].Value : header;
    }

    /// <summary>Every interface in WebView2.h, by IID, with its vtable's methods in slot order, IUnknown's included.</summary>
    private static Dictionary<Guid, (string Name, List<string> Slots)> Header()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebView2.h"));
        var interfaces = new Dictionary<Guid, (string, List<string>)>();
        foreach (Match declaration in InterfacePattern().Matches(text))
        {
            var name = declaration.Groups[2].Value;
            var start = text.IndexOf($"typedef struct {name}Vtbl", StringComparison.Ordinal);
            var end = text.IndexOf($"}} {name}Vtbl;", start, StringComparison.Ordinal);
            var slots = SlotPattern().Matches(text[start..end]).Select(slot => slot.Groups[1].Value).ToList();
            interfaces[Guid.Parse(declaration.Groups[1].Value)] = (name, slots);
        }

        return interfaces;
    }

    /// <summary>The <c>[GeneratedComInterface]</c> interfaces of Spar.Windows, with their IIDs and methods in declaration order.</summary>
    private static List<(string Name, Guid Iid, List<string> Methods)> Declared()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Spar.Windows.dll"));
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
