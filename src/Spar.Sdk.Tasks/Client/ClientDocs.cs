using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Sdk.Tasks.Client;

/// <summary>
/// The C# documentation of the project's own types, for the JSDoc of the generated modules. Types from referenced
/// assemblies have none here. Keys are the generator's C# spellings (<c>global::App.Item</c>).
/// </summary>
internal sealed class ClientDocs
{
    private static readonly SymbolDisplayFormat KeyFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Dictionary<string, INamedTypeSymbol> _types = new(StringComparer.Ordinal);

    public ClientDocs(Compilation compilation)
    {
        foreach (var type in ContractSource.AllTypes(compilation.Assembly.GlobalNamespace))
        {
            _types[type.ToDisplayString(KeyFormat)] = type;
        }
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
