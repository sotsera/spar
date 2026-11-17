using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Spar.Sdk.Tasks.Client;

/// <summary>
/// A referenced assembly's XML documentation file, the one the compiler writes beside it (<c>Lib.xml</c>), as Roslyn
/// asks for it: each member's <c>&lt;member&gt;</c> element by its documentation id (<c>T:Lib.Job</c>). The file is
/// read on the first request, which comes only for a listed library's types.
/// </summary>
internal sealed class XmlDocumentationFile : DocumentationProvider
{
    private readonly string _path;
    private readonly Lazy<Dictionary<string, string>> _members;

    public XmlDocumentationFile(string path)
    {
        _path = path;
        _members = new Lazy<Dictionary<string, string>>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public override bool Equals(object? obj) =>
        obj is XmlDocumentationFile other && string.Equals(_path, other._path, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(_path);

    protected override string? GetDocumentationForSymbol(string documentationMemberID, CultureInfo preferredCulture, CancellationToken cancellationToken = default) =>
        _members.Value.GetValueOrDefault(documentationMemberID);

    /// <summary>Every member of the file; a file that cannot be read documents nothing, as a missing one would.</summary>
    private Dictionary<string, string> Load()
    {
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(_path, settings);
            var document = XDocument.Load(reader);
            foreach (var member in document.Root?.Element("members")?.Elements("member") ?? [])
            {
                if ((string?)member.Attribute("name") is { Length: > 0 } name)
                {
                    members[name] = member.ToString(SaveOptions.DisableFormatting);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            members.Clear();
        }

        return members;
    }
}
