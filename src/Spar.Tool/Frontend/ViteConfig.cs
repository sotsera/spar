using System.Text;
using System.Text.RegularExpressions;

namespace Spar.Tool.Frontend;

/// <summary>
/// The frontend's Vite configuration and Spar's plugin in it: <c>import spar from "@spar/app/vite"</c> and <c>spar()</c>
/// in <c>plugins</c>. <c>spar doctor</c> checks it and <c>spar init frontend</c> adds it, editing the file as text only
/// where its shape is plain (one <c>plugins: [ … ]</c> array); anything else gets the snippet instead.
/// </summary>
internal static partial class ViteConfig
{
    public const string PluginModule = "@spar/app/vite";

    /// <summary>The names Vite looks for, in its order; <c>.cjs</c> and <c>.cts</c> are read but never edited.</summary>
    public static readonly string[] Names = ["vite.config.js", "vite.config.mjs", "vite.config.ts", "vite.config.cjs", "vite.config.mts", "vite.config.cts"];

    /// <summary>The snippet for a configuration Spar does not edit.</summary>
    public const string Snippet = """
        import spar from "@spar/app/vite";

        export default defineConfig({
          plugins: [/* the framework's plugin, then */ spar()],
        });
        """;

    /// <summary>The configuration file in the folder, or null when it has none.</summary>
    public static string? Find(string frontend) =>
        Names.Select(name => Path.Combine(frontend, name)).FirstOrDefault(File.Exists);

    /// <summary>Whether the configuration imports Spar's plugin, and calls it.</summary>
    public static (bool Imported, bool Called) Inspect(string text)
    {
        var masked = Mask(text);
        var name = ImportedName(text, masked);
        return name is null ? (false, false) : (true, Regex.IsMatch(masked, $@"(?<![\w$.]){Regex.Escape(name)}\s*\("));
    }

    /// <summary>
    /// The configuration with the import and <c>spar()</c> added, or the reason it was not edited. Unchanged when the
    /// plugin is already there.
    /// </summary>
    public static (string? Text, string? Problem) AddPlugin(string text, string fileName)
    {
        if (fileName.EndsWith(".cjs", StringComparison.Ordinal) || fileName.EndsWith(".cts", StringComparison.Ordinal))
        {
            return (null, $"{fileName} is a CommonJS file, and the plugin is an ES module.");
        }

        var (imported, called) = Inspect(text);
        if (imported && called)
        {
            return (text, null);
        }

        if (imported)
        {
            return (null, $"{fileName} imports {PluginModule} but does not call it in plugins.");
        }

        var masked = Mask(text);
        var arrays = PluginsArray().Matches(masked);
        if (arrays.Count != 1)
        {
            return (null, arrays.Count == 0
                ? $"{fileName} has no plugins: [ … ] array to add the plugin to."
                : $"{fileName} has {arrays.Count} plugins arrays, so it is not clear which one to change.");
        }

        var open = arrays[0].Index + arrays[0].Length - 1;
        var close = Closing(masked, open);
        if (close < 0)
        {
            return (null, $"{fileName}'s plugins array has no end that could be found.");
        }

        var name = Regex.IsMatch(masked, @"(?<![\w$.])spar(?![\w$])") ? "sparPlugin" : "spar";
        var withPlugin = InsertCall(text, open, close, $"{name}()");
        return (InsertImport(withPlugin, name), null);
    }

    /// <summary>The local name the configuration gives Spar's plugin's default export, or null when it does not import it.</summary>
    private static string? ImportedName(string text, string masked)
    {
        foreach (Match match in ImportStatement().Matches(text))
        {
            // Only an import outside comments: the masked text keeps the keyword where the code has it.
            if (masked.AsSpan(match.Index).StartsWith("import", StringComparison.Ordinal) && match.Groups["module"].Value == PluginModule)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    /// <summary>Adds the call before the array's end: on its own line in a multi-line array, else after the last item.</summary>
    private static string InsertCall(string text, int open, int close, string call)
    {
        var inside = text[(open + 1)..close];
        if (inside.Trim().Length == 0)
        {
            return text[..(open + 1)] + call + text[close..];
        }

        var lastItem = close - 1;
        while (char.IsWhiteSpace(text[lastItem]))
        {
            lastItem--;
        }

        var hasComma = text[lastItem] == ',';
        if (!inside.Contains('\n'))
        {
            var separator = hasComma ? " " : ", ";
            return text[..(lastItem + 1)] + separator + call + text[(lastItem + 1)..];
        }

        // A multi-line array: the new line takes the items' indentation and the file's line ending.
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lineStart = text.LastIndexOf('\n', lastItem) + 1;
        var indent = new string(text[lineStart..].TakeWhile(c => c is ' ' or '\t').ToArray());
        var comma = hasComma ? "" : ",";
        return text[..(lastItem + 1)] + comma + newline + indent + call + "," + text[(lastItem + 1)..];
    }

    /// <summary>Adds the import after the file's last import line, in its quotes and with its semicolons.</summary>
    private static string InsertImport(string text, string name)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var imports = ImportStatement().Matches(text);
        var quote = imports.Count > 0 && imports[0].Value.Contains('\'') ? '\'' : '"';
        var semicolon = imports.Count == 0 || imports[^1].Value.TrimEnd().EndsWith(';') ? ";" : "";
        var line = $"import {name} from {quote}{PluginModule}{quote}{semicolon}";
        if (imports.Count == 0)
        {
            return line + newline + newline + text;
        }

        var end = imports[^1].Index + imports[^1].Length;
        return text[..end] + newline + line + text[end..];
    }

    /// <summary>The bracket closing the one at <paramref name="open"/>, in masked text; -1 when there is none.</summary>
    private static int Closing(string masked, int open)
    {
        var depth = 0;
        for (var index = open; index < masked.Length; index++)
        {
            switch (masked[index])
            {
                case '[' or '(' or '{':
                    depth++;
                    break;
                case ']' or ')' or '}':
                    depth--;
                    if (depth == 0)
                    {
                        return masked[index] == ']' ? index : -1;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>
    /// The text with comments and the insides of strings and template literals replaced by spaces, so searches and
    /// bracket counts see only code; lengths and line breaks are kept, so positions are the original's.
    /// </summary>
    internal static string Mask(string text)
    {
        var masked = new StringBuilder(text);
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            int end;
            if (current == '/' && next == '/')
            {
                end = text.IndexOf('\n', index);
                end = end < 0 ? text.Length : end;
                Blank(masked, index, end);
            }
            else if (current == '/' && next == '*')
            {
                end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                Blank(masked, index, end);
            }
            else if (current is '"' or '\'' or '`')
            {
                end = StringEnd(text, index);
                Blank(masked, index + 1, Math.Max(index + 1, end - 1));
            }
            else
            {
                index++;
                continue;
            }

            index = Math.Max(end, index + 1);
        }

        return masked.ToString();
    }

    private static int StringEnd(string text, int start)
    {
        var quote = text[start];
        for (var index = start + 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
            }
            else if (text[index] == quote)
            {
                return index + 1;
            }
            else if (text[index] == '\n' && quote != '`')
            {
                return index;
            }
        }

        return text.Length;
    }

    private static void Blank(StringBuilder masked, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (masked[index] is not ('\n' or '\r'))
            {
                masked[index] = ' ';
            }
        }
    }

    /// <summary>One top-level import: its default import's name, if any, and its module.</summary>
    [GeneratedRegex(@"(?ms)^import\s+(?:(?<name>[A-Za-z_$][\w$]*)\s*,?\s*)?(?:\{[^}]*\}\s*|\*\s+as\s+[\w$]+\s*)?(?:from\s*)?(?<q>['""])(?<module>[^'""\n]+)\k<q>[ \t]*;?")]
    private static partial Regex ImportStatement();

    [GeneratedRegex(@"(?<![\w$.])plugins\s*:\s*\[")]
    private static partial Regex PluginsArray();
}
