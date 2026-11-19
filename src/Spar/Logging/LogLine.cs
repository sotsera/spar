using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>One entry for the log file, taken on the caller's thread and written by the file's own task.</summary>
internal sealed class LogEntry(DateTime at, LogLevel level, string category, string message, Exception? exception)
{
    /// <summary>When it was logged, in UTC.</summary>
    public DateTime At { get; } = at;

    public LogLevel Level { get; } = level;

    public string Category { get; } = category;

    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}

/// <summary>
/// The log file's plain-text lines (decision "Logging's public shape"): one entry starts one line with its UTC time,
/// its level in a fixed width, its category and its message, as in
/// <c>2026-11-17T22:41:07.318Z INFO  Spar.Host  message</c>. Everything else the entry carries, the further lines of a
/// message and an exception's type, message and stack, follows on lines indented by <see cref="Indent"/>, so no text
/// in an entry can make a line that looks like another entry: only an entry's first line starts in the first column.
/// Control characters other than a tab are written as <c>\uXXXX</c>, a category stays on one line, and a message
/// longer than <see cref="MaxMessage"/> is cut. Scopes and the structured values of a message are not written.
/// </summary>
internal static class LogLine
{
    /// <summary>What starts every line of an entry after its first.</summary>
    public const string Indent = "    ";

    /// <summary>The longest message written; a longer one is cut and says how much was left out.</summary>
    public const int MaxMessage = 16 * 1024;

    // The line breaks an editor may show as one: CR, LF, NEL, LS and PS.
    private const char CarriageReturn = (char)0x0D;
    private const char LineFeed = (char)0x0A;
    private const char NextLine = (char)0x85;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// The line ending: CRLF on Windows, where Notepad and support tools expect it, LF elsewhere. A property, so the
    /// trimmer folds it per OS and the image keeps no static field.
    /// </summary>
    public static string NewLine => OperatingSystem.IsWindows() ? "\r\n" : "\n";

    /// <summary>Appends <paramref name="entry"/> and its line ending to <paramref name="text"/>.</summary>
    public static void Append(StringBuilder text, LogEntry entry)
    {
        text.Append(entry.At.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Level(entry.Level))
            .Append(' ');
        AppendText(text, entry.Category, multiline: false);
        text.Append("  ");

        var message = entry.Message;
        if (message.Length > MaxMessage)
        {
            AppendText(text, message.AsSpan(0, MaxMessage), multiline: true);
            text.Append(CultureInfo.InvariantCulture, $" … ({message.Length - MaxMessage} more characters)");
        }
        else
        {
            AppendText(text, message, multiline: true);
        }

        if (entry.Exception is { } exception)
        {
            text.Append(NewLine).Append(Indent);
            AppendText(text, exception.ToString(), multiline: true);
        }

        text.Append(NewLine);
    }

    /// <summary>The level in five characters.</summary>
    public static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "CRIT ",
    };

    /// <summary>
    /// Appends text with its line breaks (CR, LF, CRLF, NEL, LS, PS) as a break and an indent, or, on one line, as
    /// <c>\n</c>; other control characters as escapes.
    /// </summary>
    private static void AppendText(StringBuilder text, ReadOnlySpan<char> value, bool multiline)
    {
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isBreak = character is CarriageReturn or LineFeed or NextLine or LineSeparator or ParagraphSeparator;
            if (!isBreak && (character >= ' ' || character == '\t') && character != '\u007f')
            {
                continue;
            }

            text.Append(value[start..index]);
            start = index + 1;
            if (!isBreak)
            {
                text.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
            }
            else if (!multiline)
            {
                text.Append("\\n");
            }
            else
            {
                if (character == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
                {
                    index++;
                    start++;
                }

                text.Append(NewLine).Append(Indent);
            }
        }

        text.Append(value[start..]);
    }
}
