using System.Buffers;
using System.Text.Json;

namespace Spar.Files;

/// <summary>
/// A request of the page's own file commands (<see cref="FileCommands"/>): a small JSON object the SDK writes, read
/// with <see cref="JsonDocument"/> (no reflection). Every field is checked; a request that is not what the SDK sends is
/// refused as <c>rejected</c> / <c>invalid-request</c>.
/// </summary>
internal sealed class FileRequest : IDisposable
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4 };

    private readonly JsonDocument _document;

    private FileRequest(JsonDocument document)
    {
        _document = document;
    }

    private JsonElement Root => _document.RootElement;

    /// <summary>The request, or null with <paramref name="problem"/> when it is not an object.</summary>
    public static FileRequest? Read(ReadOnlyMemory<byte> json, out string? problem)
    {
        problem = null;
        try
        {
            var document = JsonDocument.Parse(json.IsEmpty ? "{}"u8.ToArray() : json, Options);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                problem = "The request is not an object.";
                return null;
            }

            return new FileRequest(document);
        }
        catch (JsonException exception)
        {
            problem = exception.Message;
            return null;
        }
    }

    public void Dispose() => _document.Dispose();

    /// <summary>An optional string field; throws <see cref="FormatException"/> when it is there and not a string.</summary>
    public string? String(string name)
    {
        if (!Root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw new FormatException($"'{name}' is not a string.");
    }

    public string RequiredString(string name) => String(name) ?? throw new FormatException($"'{name}' is missing.");

    public bool? Boolean(string name)
    {
        if (!Root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"'{name}' is not a boolean."),
        };
    }

    public int? Int32(string name)
    {
        if (!Root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new FormatException($"'{name}' is not a 32-bit integer.");
    }

    public byte[]? Bytes(string name)
    {
        var text = String(name);
        if (text is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new FormatException($"'{name}' is not base64.");
        }
    }

    public IReadOnlyList<string> Strings(string name)
    {
        if (!Root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"'{name}' is not a list.");
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            list.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new FormatException($"'{name}' holds a value that is not a string."));
        }

        return list;
    }

    /// <summary>The dialog filters, <c>[{"name","extensions":["md",…]}]</c>, as C# filters with <c>*.md</c> patterns.</summary>
    public IReadOnlyList<FileFilter> Filters()
    {
        if (!Root.TryGetProperty("filters", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("'filters' is not a list.");
        }

        var filters = new List<FileFilter>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("A filter is { name, extensions }.");
            }

            var patterns = new List<string>();
            foreach (var extension in extensions.EnumerateArray())
            {
                var text = extension.ValueKind == JsonValueKind.String ? extension.GetString()! : throw new FormatException("An extension is not a string.");
                patterns.Add("*." + text.TrimStart('.'));
            }

            filters.Add(new FileFilter(name.GetString()!, string.Join(';', patterns)));
        }

        return filters;
    }

    /// <summary>Writes one JSON value into its own buffer.</summary>
    public static ReadOnlyMemory<byte> Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return buffer.WrittenMemory;
    }
}
