using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Sdk.Tasks.Client;

/// <summary>A name in a C# file: the file relative to the project, and its 0-based line and column.</summary>
internal readonly record struct SourceSpot(string File, int Line, int Column);

/// <summary>
/// The C# documentation of the project's types and of the libraries it lists in <c>SparContracts</c>, for the JSDoc of
/// the generated modules. A library's comments come from the XML documentation file beside its assembly, when it has
/// one (<see cref="ContractSource"/>); its types have no place in the project's source. Keys are the generator's C#
/// spellings (<c>global::App.Item</c>).
/// </summary>
internal sealed class ClientDocs
{
    private static readonly SymbolDisplayFormat KeyFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Dictionary<string, INamedTypeSymbol> _types = new(StringComparer.Ordinal);
    private readonly string _projectDirectory;

    public ClientDocs(Compilation compilation, string projectDirectory, IReadOnlyList<string> libraries)
    {
        _projectDirectory = projectDirectory;
        foreach (var type in ContractSource.AllTypes(compilation.Assembly.GlobalNamespace))
        {
            _types[type.ToDisplayString(KeyFormat)] = type;
        }

        // The project's own type keeps its name when a library declares one spelled the same.
        var listed = new HashSet<string>(libraries, StringComparer.OrdinalIgnoreCase);
        foreach (var library in compilation.SourceModule.ReferencedAssemblySymbols.Where(a => listed.Contains(a.Name)))
        {
            foreach (var type in ContractSource.AllTypes(library.GlobalNamespace))
            {
                _types.TryAdd(type.ToDisplayString(KeyFormat), type);
            }
        }
    }

    /// <summary>Where the project declares a type: its file relative to the project, and its 1-based line.</summary>
    public (string File, int Line)? Location(string csharp) =>
        TypeSpot(csharp, out var spot) ? (spot.File, spot.Line + 1) : null;

    /// <summary>The name of a type in the project's source, for the declaration maps.</summary>
    public bool TypeSpot(string csharp, out SourceSpot spot) => Spot(_types.GetValueOrDefault(csharp), out spot);

    /// <summary>The name of a method, by its C# name.</summary>
    public bool MethodSpot(string typeCSharp, string method, out SourceSpot spot) =>
        Spot(_types.GetValueOrDefault(typeCSharp)?.GetMembers(method).FirstOrDefault(), out spot);

    /// <summary>A positional record's parameter, which declares the property.</summary>
    public bool ParameterSpot(string recordCSharp, string parameter, out SourceSpot spot)
    {
        var declared = _types.GetValueOrDefault(recordCSharp)?.InstanceConstructors
            .SelectMany(c => c.Parameters)
            .FirstOrDefault(p => p.Name == parameter && p.Locations.Any(l => l.IsInSource));
        return Spot(declared, out spot);
    }

    private bool Spot(ISymbol? symbol, out SourceSpot spot)
    {
        var location = symbol?.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null)
        {
            spot = default;
            return false;
        }

        var start = location.GetLineSpan().StartLinePosition;
        var file = Path.GetRelativePath(_projectDirectory, location.SourceTree.FilePath).Replace('\\', '/');
        spot = new SourceSpot(file, start.Line, start.Character);
        return true;
    }

    /// <summary>A type's summary.</summary>
    public string? Type(string csharp) => _types.TryGetValue(csharp, out var type) ? Summary(type) : null;

    /// <summary>A method's summary, by its C# name.</summary>
    public string? Method(string typeCSharp, string method) =>
        _types.TryGetValue(typeCSharp, out var type) ? Summary(type.GetMembers(method).FirstOrDefault()) : null;

    /// <summary>A positional record's value: its <c>&lt;param&gt;</c> on the record.</summary>
    public string? Parameter(string recordCSharp, string parameter)
    {
        if (!_types.TryGetValue(recordCSharp, out var type))
        {
            return null;
        }

        var xml = Parse(type);
        var element = xml?.Elements("param").FirstOrDefault(p => (string?)p.Attribute("name") == parameter);
        return element is null ? null : Text(element);
    }

    private static string? Summary(ISymbol? symbol)
    {
        var summary = symbol is null ? null : Parse(symbol)?.Element("summary");
        return summary is null ? null : Text(summary);
    }

    private static XElement? Parse(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: false);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            return XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return null; // Malformed documentation has a compiler warning of its own; the module just goes without it.
        }
    }

    /// <summary>The text of a documentation element on one line: <c>&lt;c&gt;</c> as code, <c>cref</c>s by name.</summary>
    private static string? Text(XElement element)
    {
        var text = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            text.Append(node switch
            {
                XText plain => plain.Value,
                XElement { Name.LocalName: "c" or "code" } code => $"`{code.Value}`",
                XElement reference when reference.Attribute("cref") is { } cref => $"`{LastName(cref.Value)}`",
                XElement reference when reference.Attribute("langword") is { } word => $"`{word.Value}`",
                XElement other => other.Value,
                _ => "",
            });
        }

        var oneLine = string.Join(' ', text.ToString().Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length == 0 ? null : oneLine;
    }

    /// <summary><c>T:App.Orders.Order</c> becomes <c>Order</c>.</summary>
    private static string LastName(string cref)
    {
        var name = cref.Contains(':', StringComparison.Ordinal) ? cref[(cref.IndexOf(':', StringComparison.Ordinal) + 1)..] : cref;
        var parenthesis = name.IndexOf('(', StringComparison.Ordinal);
        name = parenthesis >= 0 ? name[..parenthesis] : name;
        return name[(name.LastIndexOf('.') + 1)..];
    }
}
