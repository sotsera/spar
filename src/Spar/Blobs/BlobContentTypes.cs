namespace Spar;

/// <summary>
/// How a blob's media type is served (decision "M3b's decide-first questions", item 2): with its own type for media the
/// page shows (images, audio, video, plain text, CSV, JSON, PDF), and as a download otherwise, so that no blob, whatever
/// type a command gave it, renders as a page of the app's origin. HTML, XML and every script type are always downloads.
/// </summary>
internal static class BlobContentTypes
{
    private static readonly string[] InlineTypes = ["text/plain", "text/csv", "application/json", "application/pdf"];

    private static readonly string[] InlineFamilies = ["image/", "audio/", "video/"];

    /// <summary>A <c>type/subtype</c> with an optional parameter list, in the characters RFC 9110 allows a token.</summary>
    public static bool IsWellFormed(string contentType)
    {
        var essence = Essence(contentType);
        var slash = essence.IndexOf('/');
        if (slash <= 0 || slash == essence.Length - 1 || essence.IndexOf('/', slash + 1) >= 0)
        {
            return false;
        }

        foreach (var c in contentType)
        {
            // No line breaks or other controls: the type goes into a response header as it is.
            if (char.IsControl(c) || c > '~')
            {
                return false;
            }
        }

        return essence.All(c => char.IsAsciiLetterOrDigit(c) || "!#$&-^_.+/".Contains(c));
    }

    /// <summary>Whether the browser may show it in place; otherwise it is served with <c>Content-Disposition: attachment</c>.</summary>
    public static bool IsInline(string contentType)
    {
        var essence = Essence(contentType).ToLowerInvariant();
        if (essence == "image/svg+xml")
        {
            // An SVG opened as a document runs its scripts; in an <img> it shows the same as a download would not.
            return false;
        }

        return InlineTypes.Contains(essence) || InlineFamilies.Any(family => essence.StartsWith(family, StringComparison.Ordinal));
    }

    /// <summary>The <c>Content-Disposition</c> value for a download, with the name in both forms of RFC 6266.</summary>
    public static string Attachment(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "attachment";
        }

        var plain = new string([.. name.Select(c => c is >= ' ' and < '\u007f' and not '"' and not '\\' ? c : '_')]);
        return $"attachment; filename=\"{plain}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    private static string Essence(string contentType)
    {
        var semicolon = contentType.IndexOf(';');
        return (semicolon < 0 ? contentType : contentType[..semicolon]).Trim();
    }
}
