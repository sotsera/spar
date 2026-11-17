using System.Text;
using Spar.Generator;

namespace Spar.Sdk.Tasks.Client;

/// <summary>Names and literals as they must appear in generated TypeScript and JavaScript.</summary>
internal static class TypeScriptNames
{
    /// <summary>The SDK types every declaration file imports, in that order (the generator checks type names against them).</summary>
    public static IReadOnlyList<string> SdkImports => ClientNames.SdkImports;

    public static bool IsReserved(string name) => ClientNames.IsReserved(name);

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
