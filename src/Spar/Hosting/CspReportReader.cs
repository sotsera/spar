using System.Globalization;
using System.Text.Json;

namespace Spar.Hosting;

/// <summary>
/// One group of Content Security Policy violations: what was blocked, by which directive, from where in the page, and
/// how many times. Every text is the page's and untrusted, cut to <see cref="CspReportReader.MaxText"/> characters.
/// </summary>
internal readonly record struct CspViolation(string Directive, string Blocked, string Source, int Line, int Column, string Sample, long Count)
{
    /// <summary>What groups violations: the directive, the blocked address and the source location (plan section 3b).</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Directive}\n{Blocked}\n{Source}:{Line}:{Column}");

    /// <summary>Where in the page it happened, for the log: <c>file:line:column</c>, or the page when unknown.</summary>
    public string Where => Source.Length == 0 ? "the page" : string.Create(CultureInfo.InvariantCulture, $"{Source}:{Line}:{Column}");
}

/// <summary>
/// Reads the two violation channels (plan section 3b) without trusting them: a batch from the host-injected listener
/// (<c>{"groups":[…],"overflow":N}</c>, with <c>"stopped":true</c> once it stopped listening) and a browser report posted to <c>report-uri</c> (<c>{"csp-report":{…}}</c>).
/// Unknown properties are skipped, texts cut short, numbers clamped, and anything malformed is refused whole. It runs
/// off the window's UI thread.
/// </summary>
internal static class CspReportReader
{
    /// <summary>The longest text kept from a report: enough for an address and a short sample, never a flood of text.</summary>
    public const int MaxText = 256;

    /// <summary>The most groups read from one batch; the listener sends fewer, so more is a page sending its own.</summary>
    public const int MaxGroupsPerBatch = 100;

    private static readonly JsonReaderOptions Options = new() { MaxDepth = 8 };

    /// <summary>Reads a listener batch into <paramref name="groups"/>; false, with nothing added, when it is malformed.</summary>
    public static bool TryReadBatch(ReadOnlySpan<byte> json, List<CspViolation> groups, out long overflow) =>
        TryReadBatch(json, groups, out overflow, out _);

    /// <summary>The same, and whether the listener says it stopped listening for its document.</summary>
    public static bool TryReadBatch(ReadOnlySpan<byte> json, List<CspViolation> groups, out long overflow, out bool stopped)
    {
        overflow = 0;
        stopped = false;
        var read = new List<CspViolation>();
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

    /// <summary>Reads one <c>report-uri</c> report; false when it is malformed or names no directive.</summary>
    public static bool TryReadReport(ReadOnlySpan<byte> json, out CspViolation violation)
    {
        violation = default;
        try
        {
            var reader = new Utf8JsonReader(json, Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            var found = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isReport = reader.ValueTextEquals("csp-report"u8);
                reader.Read();
                if (isReport && reader.TokenType == JsonTokenType.StartObject)
                {
                    found = ReadReport(ref reader, out violation);
                }
                else
                {
                    reader.Skip();
                }
            }

            return found && reader.TokenType == JsonTokenType.EndObject && !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ReadGroups(ref Utf8JsonReader reader, List<CspViolation> into)
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
            if (into.Count < MaxGroupsPerBatch && group.Directive.Length > 0)
            {
                into.Add(group);
            }
        }

        return true;
    }

    private static CspViolation ReadGroup(ref Utf8JsonReader reader)
    {
        string directive = "", blocked = "", source = "", sample = "";
        int line = 0, column = 0;
        long count = 1;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("directive"u8))
            {
                directive = Text(ref reader);
            }
            else if (reader.ValueTextEquals("blocked"u8))
            {
                blocked = Text(ref reader);
            }
            else if (reader.ValueTextEquals("source"u8))
            {
                source = Text(ref reader);
            }
            else if (reader.ValueTextEquals("line"u8))
            {
                line = Position(ref reader);
            }
            else if (reader.ValueTextEquals("column"u8))
            {
                column = Position(ref reader);
            }
            else if (reader.ValueTextEquals("sample"u8))
            {
                sample = Text(ref reader);
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

        return new CspViolation(directive, blocked, source, line, column, sample, count);
    }

    private static bool ReadReport(ref Utf8JsonReader reader, out CspViolation violation)
    {
        string effective = "", violated = "", blocked = "", source = "", sample = "";
        int line = 0, column = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("effective-directive"u8))
            {
                effective = Text(ref reader);
            }
            else if (reader.ValueTextEquals("violated-directive"u8))
            {
                violated = Text(ref reader);
            }
            else if (reader.ValueTextEquals("blocked-uri"u8))
            {
                blocked = Text(ref reader);
            }
            else if (reader.ValueTextEquals("source-file"u8))
            {
                source = Text(ref reader);
            }
            else if (reader.ValueTextEquals("line-number"u8))
            {
                line = Position(ref reader);
            }
            else if (reader.ValueTextEquals("column-number"u8))
            {
                column = Position(ref reader);
            }
            else if (reader.ValueTextEquals("script-sample"u8))
            {
                sample = Text(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        // Older engines name only the violated directive, which may carry its sources after the name.
        var directive = effective.Length > 0 ? effective : FirstWord(violated);
        violation = new CspViolation(directive, blocked, source, line, column, sample, 1);
        return directive.Length > 0;
    }

    private static string Text(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return "";
        }

        var text = reader.GetString() ?? "";
        return text.Length <= MaxText ? text : text[..MaxText];
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

    private static string FirstWord(string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }
}
