using System.Text;
using System.Text.Json;

namespace Spar.Tool.Frontend;

/// <summary>
/// The frontend's <c>package.json</c>: what <c>spar doctor</c> checks (the <c>@spar/app</c> dependency, the <c>dev</c>
/// and <c>build</c> scripts) and the edits <c>spar init frontend</c> makes, as text, so the file keeps its own layout:
/// a property is added with its object's indentation and the file's line ending, or a value is replaced in place.
/// </summary>
internal static class PackageJson
{
    public const string SparPackage = "@spar/app";

    /// <summary>What the file says, or the reason it cannot be read.</summary>
    public static (Contents? Contents, string? Problem) Read(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "package.json is not a JSON object.");
            }

            return (new Contents(
                Value(root, "dependencies", SparPackage),
                Value(root, "devDependencies", SparPackage),
                Value(root, "scripts", "dev"),
                Value(root, "scripts", "build")), null);
        }
        catch (JsonException exception)
        {
            return (null, $"package.json is not valid JSON: {exception.Message}");
        }
    }

    /// <summary>
    /// The <c>file:</c> dependency that names the app's generated client from the frontend folder:
    /// <c>file:../obj/spar/client</c> for the default layout, with <c>/</c> on every OS.
    /// </summary>
    public static string ClientSpecifier(string frontendDirectory, string clientDirectory) =>
        "file:" + Path.GetRelativePath(frontendDirectory, clientDirectory).Replace('\\', '/');

    /// <summary>Whether a <c>file:</c> specifier names <paramref name="clientDirectory"/>, from the frontend folder.</summary>
    public static bool NamesClient(string? specifier, string frontendDirectory, string clientDirectory)
    {
        if (specifier is null || !specifier.StartsWith("file:", StringComparison.Ordinal))
        {
            return false;
        }

        var target = Path.GetFullPath(Path.Combine(frontendDirectory, specifier["file:".Length..]));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(target), Path.TrimEndingDirectorySeparator(Path.GetFullPath(clientDirectory)), comparison);
    }

    /// <summary>
    /// <paramref name="text"/> with <c>object.name</c> set to <paramref name="value"/>: replaced where it is, added as the
    /// object's first property (<paramref name="first"/>) or its last, and the object added after the root's last
    /// property when there is none.
    /// </summary>
    public static string Set(string text, string objectName, string name, string value, bool first)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var layout = Layout.Of(bytes);
        var quoted = JsonSerializerString(value);
        if (layout.Objects.TryGetValue(objectName, out var found))
        {
            if (found.Values.TryGetValue(name, out var existing))
            {
                return Splice(bytes, existing.Start, existing.End, quoted);
            }

            var indent = found.ItemIndent ?? layout.Indent + layout.Indent;
            var entry = $"{indent}{JsonSerializerString(name)}: {quoted}";
            if (found.Values.Count == 0)
            {
                return Splice(bytes, found.Open + 1, found.Close, newline + entry + newline + layout.Indent);
            }

            if (first)
            {
                return Splice(bytes, found.Open + 1, found.Open + 1, newline + entry + ",");
            }

            return Splice(bytes, found.LastValueEnd, found.LastValueEnd, "," + newline + entry);
        }

        var block = $"{layout.Indent}{JsonSerializerString(objectName)}: {{{newline}{layout.Indent}{layout.Indent}{JsonSerializerString(name)}: {quoted}{newline}{layout.Indent}}}";
        return layout.RootLastValueEnd < 0
            ? Splice(bytes, layout.RootOpen + 1, layout.RootOpen + 1, newline + block + newline)
            : Splice(bytes, layout.RootLastValueEnd, layout.RootLastValueEnd, "," + newline + block);
    }

    private static string? Value(JsonElement root, string objectName, string name) =>
        root.TryGetProperty(objectName, out var container) && container.ValueKind == JsonValueKind.Object
            && container.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Splice(byte[] bytes, int start, int end, string insert) =>
        Encoding.UTF8.GetString(bytes, 0, start) + insert + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);

    private static string JsonSerializerString(string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStringValue(value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>What <c>package.json</c> says about Spar.</summary>
    public sealed record Contents(string? SparDependency, string? SparDevDependency, string? DevScript, string? BuildScript);

    /// <summary>Where the root's properties and its objects' properties are, as byte offsets.</summary>
    private sealed class Layout
    {
        public Dictionary<string, ObjectSpan> Objects { get; } = new(StringComparer.Ordinal);

        public int RootOpen { get; private set; }

        public int RootLastValueEnd { get; private set; } = -1;

        /// <summary>One level of indentation, from the root's first property; two spaces when it has none.</summary>
        public string Indent { get; private set; } = "  ";

        public static Layout Of(byte[] bytes)
        {
            var layout = new Layout();
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            reader.Read();
            layout.RootOpen = (int)reader.TokenStartIndex;
            var indentRead = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (!indentRead)
                {
                    layout.Indent = IndentAt(bytes, (int)reader.TokenStartIndex) is { Length: > 0 } indent ? indent : "  ";
                    indentRead = true;
                }

                var name = reader.GetString()!;
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 1)
                {
                    layout.Objects[name] = ReadObject(ref reader, bytes);
                }
                else
                {
                    reader.Skip();
                }

                layout.RootLastValueEnd = (int)reader.BytesConsumed;
            }

            return layout;
        }

        private static ObjectSpan ReadObject(ref Utf8JsonReader reader, byte[] bytes)
        {
            var span = new ObjectSpan { Open = (int)reader.TokenStartIndex };
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                span.ItemIndent ??= IndentAt(bytes, (int)reader.TokenStartIndex);
                var name = reader.GetString()!;
                reader.Read();
                var start = (int)reader.TokenStartIndex;
                reader.Skip();
                var end = (int)reader.BytesConsumed;
                span.Values[name] = (start, end);
                span.LastValueEnd = end;
            }

            span.Close = (int)reader.TokenStartIndex;
            return span;
        }

        /// <summary>The spaces and tabs before a token on its line, or null when something else is before it.</summary>
        private static string? IndentAt(byte[] bytes, int index)
        {
            var start = index;
            while (start > 0 && bytes[start - 1] is (byte)' ' or (byte)'\t')
            {
                start--;
            }

            return start == 0 || bytes[start - 1] == (byte)'\n' ? Encoding.ASCII.GetString(bytes, start, index - start) : null;
        }
    }

    private sealed class ObjectSpan
    {
        public int Open { get; init; }

        public int Close { get; set; }

        public int LastValueEnd { get; set; }

        public string? ItemIndent { get; set; }

        public Dictionary<string, (int Start, int End)> Values { get; } = new(StringComparer.Ordinal);
    }
}
