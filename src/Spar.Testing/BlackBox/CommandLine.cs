using System.Text;

namespace Spar.Testing;

/// <summary>
/// A command line that <c>CommandLineToArgvW</c> and the C runtime split back into exactly the executable and the
/// arguments given: an argument with a space, a tab, a quote or nothing at all is quoted, and the backslashes before a
/// quote are doubled.
/// </summary>
internal static class CommandLine
{
    public static string Build(string executable, IReadOnlyList<string> arguments)
    {
        if (executable.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("An executable path cannot contain a quote.", nameof(executable));
        }

        var text = new StringBuilder().Append('"').Append(executable).Append('"');
        foreach (var argument in arguments)
        {
            text.Append(' ');
            Append(text, argument);
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            text.Append(argument);
            return;
        }

        text.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                // Each backslash before a quote is doubled, and the quote itself escaped.
                text.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                text.Append('\\', backslashes);
            }

            backslashes = 0;
            text.Append(character);
        }

        // Backslashes before the closing quote are doubled, so it stays a quote.
        text.Append('\\', backslashes * 2).Append('"');
    }
}
