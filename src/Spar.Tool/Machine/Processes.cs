using System.Diagnostics;
using System.Text;

namespace Spar.Tool.Machine;

/// <summary>
/// How the tool starts a program: by its full path from <c>PATH</c>, and on Windows a <c>.cmd</c> script, such as npm's
/// <c>npm.cmd</c>, through the command interpreter (started by its bare name, npm's script looks for npm in the working
/// folder's <c>node_modules</c>).
/// </summary>
internal static class Processes
{
    public static ProcessStartInfo StartInfo(string program, IReadOnlyList<string> arguments)
    {
        var resolved = Resolve(program) ?? program;
        var extension = Path.GetExtension(resolved);
        if (OperatingSystem.IsWindows() && (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // /s takes the outer quotes away and runs the rest as written: "\"C:\...\npm.cmd\" install …".
            var line = new StringBuilder().Append("\"\"").Append(resolved).Append('"');
            foreach (var argument in arguments)
            {
                line.Append(' ').Append(Quote(argument));
            }

            line.Append('"');
            var interpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return new ProcessStartInfo(interpreter) { Arguments = $"/d /s /c {line}", UseShellExecute = false };
        }

        var start = new ProcessStartInfo(resolved) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    /// <summary>The program's full path from <c>PATH</c> (with Windows' extensions), or null when it is not there.</summary>
    public static string? Resolve(string program)
    {
        if (Path.IsPathRooted(program))
        {
            return File.Exists(program) ? program : null;
        }

        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder.Trim('"'), program + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>An argument for the command interpreter: quoted when it has a space or a character cmd treats specially.</summary>
    private static string Quote(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', '\t', '&', '|', '<', '>', '^', '(', ')']) < 0 ? argument : $"\"{argument}\"";
}
