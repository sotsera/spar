using System.Text;

namespace Spar.Sdk.Tasks.Client;

/// <summary>Names and literals as they must appear in generated TypeScript and JavaScript.</summary>
internal static class TypeScriptNames
{
    /// <summary>
    /// The SDK types every declaration file imports, in that order. The import line is written from this list, so a
    /// contract type can never shadow an import the list does not know about.
    /// </summary>
    public static readonly IReadOnlyList<string> SdkImports = ["Call", "CallOptions"];

    /// <summary>Reserved words and strict-mode restrictions of ECMAScript modules, and TypeScript's own type keywords.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements", "import", "in",
        "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return",
        "static", "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
        "arguments", "eval", "undefined", "NaN", "Infinity", "any", "boolean", "never", "number", "object", "string",
        "symbol", "unknown", "bigint",
    };

    public static bool IsReserved(string name) => Reserved.Contains(name);

    /// <summary>A parameter name that is legal in a function signature.</summary>
    public static string Parameter(string name) => IsReserved(name) ? name + "Value" : name;

    /// <summary>A JSON string literal, which is also a valid JavaScript one.</summary>
    public static string Literal(string text)
    {
        var literal = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case < ' ' or '\u2028' or '\u2029':
                    literal.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    literal.Append(c);
                    break;
            }
        }

        return literal.Append('"').ToString();
    }

    /// <summary>Documentation text that cannot end the comment it sits in.</summary>
    public static string Doc(string text) => text.Replace("*/", "*\\/", StringComparison.Ordinal);
}
