using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Spar.StaticState;

/// <summary>
/// Spar keeps no process-wide mutable state (plan section 5, "Rules for Spar's own code"): several apps run in one
/// test process, and the build runs tasks of several projects in one MSBuild node. Every static field of Spar's
/// assemblies is listed in <c>tests/static-state/reviewed.txt</c> with why it is safe; a field added, removed or made
/// writable fails this test until the list says so. The fields are read from the assemblies' metadata, so the check
/// sees every one, and it runs where the test's output holds the assemblies (the CoreCLR lane). This file is linked
/// into each test project whose output holds one of them.
/// </summary>
public sealed class StaticStateTests
{
    /// <summary>Spar's assemblies; each test project checks the ones in its output.</summary>
    private static readonly string[] SparAssemblies =
    [
        "Spar", "Spar.Windows", "Spar.Loopback", "Spar.Testing", "Spar.Testing.Browser", "Spar.Generator", "Spar.Sdk.Tasks",
    ];

    [Fact]
    public void Every_static_field_of_Spars_assemblies_is_reviewed()
    {
        var present = SparAssemblies.Where(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll"))).ToList();
        Assert.NotEmpty(present);

        var reviewed = Reviewed();
        var problems = new List<string>();
        foreach (var assembly in present)
        {
            var actual = Fields(Path.Combine(AppContext.BaseDirectory, assembly + ".dll"));
            var expected = reviewed.Where(r => r.Assembly == assembly).ToDictionary(r => r.Field, r => r, StringComparer.Ordinal);
            foreach (var (field, writable) in actual.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                if (!expected.Remove(field, out var entry))
                {
                    problems.Add($"not reviewed: {assembly} | {field} | {(writable ? "writable" : "readonly")} | <why it is safe>");
                }
                else if (entry.Writable != writable)
                {
                    problems.Add($"now {(writable ? "writable" : "readonly")}: {assembly} | {field}");
                }
            }

            problems.AddRange(expected.Keys.Select(field => $"no longer exists, remove it: {assembly} | {field}"));
        }

        Assert.True(problems.Count == 0, "tests/static-state/reviewed.txt is out of date:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_reviewed_field_says_why_it_is_safe()
    {
        var unexplained = Reviewed().Where(r => r.Reason.Length == 0 || r.Reason.StartsWith('<')).Select(r => $"{r.Assembly} | {r.Field}");

        Assert.Empty(unexplained);
    }

    /// <summary>The static fields of one assembly, by declaring type and name, and whether each can be assigned.</summary>
    private static Dictionary<string, bool> Fields(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var fields = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            var typeName = FullName(metadata, type);
            if (IsCompilerData(typeName))
            {
                continue;
            }

            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                var attributes = field.Attributes;
                if ((attributes & FieldAttributes.Static) == 0 || (attributes & FieldAttributes.Literal) != 0)
                {
                    continue;
                }

                fields[$"{typeName}.{metadata.GetString(field.Name)}"] = (attributes & FieldAttributes.InitOnly) == 0;
            }
        }

        return fields;
    }

    /// <summary>
    /// The compiler's own types: the module, the data behind literals, and the caches of lambdas (<c>&lt;&gt;c</c>) and of
    /// method-group delegates (<c>&lt;&gt;O</c>), which hold delegates created once and never change what a call does.
    /// Their fields are not Spar's state. Types a source generator writes stay in the list.
    /// </summary>
    private static bool IsCompilerData(string typeName) =>
        typeName == "<Module>"
        || typeName.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal)
        || typeName.Split('+', '.').Any(part => part.StartsWith("<>c", StringComparison.Ordinal) || part.StartsWith("<>O", StringComparison.Ordinal));

    /// <summary>
    /// A type's name as the list spells it. A file-local type, such as the regex generator's, is named after a hash of
    /// its source file's path (<c>&lt;RegexGenerator_g&gt;F298E…__Utilities</c>), which differs between checkouts and
    /// between builds with and without <c>ContinuousIntegrationBuild</c>'s path map; the list has <c>{file}</c> instead.
    /// </summary>
    private static string FullName(MetadataReader metadata, TypeDefinition type)
    {
        var name = System.Text.RegularExpressions.Regex.Replace(metadata.GetString(type.Name), "^(<[^>]+>)F[0-9A-F]{64}__", "$1F{file}__");
        if (type.GetDeclaringType() is { IsNil: false } declaring)
        {
            return FullName(metadata, metadata.GetTypeDefinition(declaring)) + "+" + name;
        }

        var space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    /// <summary>The reviewed list: <c>assembly | field | readonly or writable | why it is safe</c>, one per line.</summary>
    private static List<(string Assembly, string Field, bool Writable, string Reason)> Reviewed()
    {
        var list = new List<(string, string, bool, string)>();
        foreach (var line in File.ReadAllLines(ListPath()))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', 4, StringSplitOptions.TrimEntries);
            Assert.True(parts.Length == 4 && parts[2] is "readonly" or "writable", $"Malformed line in reviewed.txt: {line}");
            list.Add((parts[0], parts[1], parts[2] == "writable", parts[3]));
        }

        return list;
    }

    private static string ListPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "static-state", "reviewed.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("tests/static-state/reviewed.txt was not found above the test's output folder.");
    }
}
