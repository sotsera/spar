using System.Globalization;
using System.Text.Json;

namespace Spar.Logging;

/// <summary>
/// One group of the page's errors: its kind (<c>console</c> for <c>console.error</c>, <c>error</c> for an uncaught
/// exception, <c>rejection</c> for an unhandled rejection), its text, where in the page it happened, the first stack
/// seen, and how many times. Every text is the page's and untrusted, cut short by <see cref="PageErrorReader"/>.
/// </summary>
internal sealed class PageError(string kind, string message, string source, int line, int column, string stack, long count)
{
    public string Kind { get; } = kind;

    public string Message { get; } = message;

    public string Source { get; } = source;

    public int Line { get; } = line;

    public int Column { get; } = column;

    public string Stack { get; } = stack;

    public long Count { get; } = count;

    /// <summary>What groups errors: kind, text and source location.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Kind}\n{Message}\n{Source}:{Line}:{Column}");

    /// <summary>Where in the page it happened, for the log: <c>file:line:column</c>, or the page when unknown.</summary>
    public string Where => Source.Length == 0 ? "the page" : string.Create(CultureInfo.InvariantCulture, $"{Source}:{Line}:{Column}");

    /// <summary>The kind as the log names it.</summary>
    public string KindName => Kind switch
    {
        PageErrorReader.Console => "console.error",
        PageErrorReader.Uncaught => "Uncaught exception",
        _ => "Unhandled rejection",
    };
}

/// <summary>
/// Reads a batch of the page's errors from the host-injected script without trusting it:
/// <c>{"groups":[{"kind":…,"message":…,"source":…,"line":…,"column":…,"stack":…,"count":…}],"overflow":N}</c>, with
/// <c>"stopped":true</c> once the script stopped reporting for its document. Unknown properties are skipped, texts cut
/// short, numbers clamped, groups of an unknown kind dropped, and anything malformed is refused whole. It runs off the
/// window's UI thread.
/// </summary>
internal static class PageErrorReader
{
    /// <summary>The kind of a <c>console.error</c> call.</summary>
    public const string Console = "console";

    /// <summary>The kind of an uncaught exception (the window's <c>error</c> event).</summary>
    public const string Uncaught = "error";

    /// <summary>The kind of an unhandled rejection (the window's <c>unhandledrejection</c> event).</summary>
    public const string Rejection = "rejection";

    /// <summary>The longest message kept.</summary>
    public const int MaxMessage = 512;

    /// <summary>The longest source address kept.</summary>
    public const int MaxSource = 256;

    /// <summary>The longest stack kept: its first frames.</summary>
    public const int MaxStack = 2048;

    /// <summary>The most groups read from one batch; the script sends fewer, so more is a page sending its own.</summary>
    public const int MaxGroupsPerBatch = 100;

    private static readonly JsonReaderOptions Options = new() { MaxDepth = 8 };

    /// <summary>Reads a batch into <paramref name="groups"/>; false, with nothing added, when it is malformed.</summary>
    public static bool TryReadBatch(ReadOnlySpan<byte> json, List<PageError> groups, out long overflow, out bool stopped)
    {
        overflow = 0;
        stopped = false;
        var read = new List<PageError>();
        try
        {
            var reader = new Utf8JsonReader(json, Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("groups"u8))
                {
                    if (!ReadGroups(ref reader, read))
                    {
                        return false;
                    }
                }
                else if (reader.ValueTextEquals("overflow"u8))
                {
                    reader.Read();
                    overflow = Count(ref reader);
                }
                else if (reader.ValueTextEquals("stopped"u8))
                {
                    reader.Read();
                    stopped = reader.TokenType == JsonTokenType.True;
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        groups.AddRange(read);
        return true;
    }

    private static bool ReadGroups(ref Utf8JsonReader reader, List<PageError> into)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var group = ReadGroup(ref reader);
            if (into.Count < MaxGroupsPerBatch && group.Kind is Console or Uncaught or Rejection)
            {
                into.Add(group);
            }
        }

        return true;
    }

    private static PageError ReadGroup(ref Utf8JsonReader reader)
    {
        string kind = "", message = "", source = "", stack = "";
        int line = 0, column = 0;
        long count = 1;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("kind"u8))
            {
                kind = Text(ref reader, 16);
            }
            else if (reader.ValueTextEquals("message"u8))
            {
                message = Text(ref reader, MaxMessage);
            }
            else if (reader.ValueTextEquals("source"u8))
            {
                source = Text(ref reader, MaxSource);
            }
            else if (reader.ValueTextEquals("line"u8))
            {
                line = Position(ref reader);
            }
            else if (reader.ValueTextEquals("column"u8))
            {
                column = Position(ref reader);
            }
            else if (reader.ValueTextEquals("stack"u8))
            {
                stack = Text(ref reader, MaxStack);
            }
            else if (reader.ValueTextEquals("count"u8))
            {
                reader.Read();
                count = Math.Max(1, Count(ref reader));
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return new PageError(kind, message, source, line, column, stack, count);
    }

    private static string Text(ref Utf8JsonReader reader, int limit)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return "";
        }

        var text = reader.GetString() ?? "";
        return text.Length <= limit ? text : text[..limit];
    }

    private static int Position(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) && value > 0 ? value : 0;
    }

    private static long Count(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var value) || value < 0)
        {
            reader.Skip();
            return 0;
        }

        // A page may claim any count; a billion is beyond what one interval can raise.
        return Math.Min(value, 1_000_000_000);
    }
}
