using System.Reflection;
using Spar.Tool;
using Spar.Tool.Devtools;
using Spar.Tool.Doctor;
using Spar.Tool.Init;
using Spar.Tool.Machine;

// The spar command (plan, M1d step 6; the devtools of M2a): dotnet spar doctor, dotnet spar init frontend, dotnet spar
// tree and the other devtools, through the app's local tool manifest; dnx Spar.Tool … without one.
var version = typeof(ExitCodes).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
try
{
    // Redirected output (an agent's, a script's, a file) is UTF-8, not the console's code page, so a page's text arrives whole;
    // a console window is written to in Unicode by .NET either way.
    var output = Console.IsOutputRedirected ? Utf8(Console.OpenStandardOutput()) : Console.Out;
    var error = Console.IsErrorRedirected ? Utf8(Console.OpenStandardError()) : Console.Error;
    return Cli.Run(args, version, output, error, Console.In, !Console.IsInputRedirected, new RealMachine(), new RealNpm(), Environment.CurrentDirectory);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"spar: {exception.Message}");
    return ExitCodes.Internal;
}

static TextWriter Utf8(Stream stream) => new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

namespace Spar.Tool
{
    /// <summary>The command line: a command and its options, help and the version.</summary>
    internal static class Cli
    {
        public const string Usage = """
            Usage: spar <command> [options]

            Commands:
              doctor          Check the machine, the Spar app's project and the build's identity.
              init frontend   Wire a Vite frontend into a Spar app, or make one first (--framework react|vue).
              dev             Build the app's Development build, run it, and rebuild it on spar reload (spar dev --help).
              reload          Ask the running spar dev to build again and restart the app.
              tree, screenshot, click, fill, wait, contracts, call, calls, logs, version
                              The devtools of the app's running Development build: spar tree --help lists them.

            Options:
              --version       Show the tool's version.
              -h, --help      Show this help; spar <command> --help shows a command's.

            In an app made with dotnet new spar-app, run it through the app's local tool manifest: dotnet tool restore
            once, then dotnet spar doctor. Without a manifest: dnx Spar.Tool doctor.
            """;

        public static int Run(
            string[] args, string version, TextWriter output, TextWriter error, TextReader input, bool interactive, IMachine machine, INpm npm,
            string workingDirectory, IDevtoolsTransport? devtools = null)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "-?" or "help")
            {
                output.WriteLine(Usage);
                return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Success;
            }

            var rest = args[1..];
            switch (args[0])
            {
                case "--version":
                    output.WriteLine(version);
                    return ExitCodes.Success;
                case "doctor":
                    return DoctorCommand.Run(rest, machine, output, error, version, workingDirectory);
                case "init":
                    return InitCommand.Run(rest, new InitConsole(output, error, input, interactive), npm, workingDirectory);
                case "dev":
                    return Dev.DevCommand.RunDev(rest, output, error, workingDirectory);
                case "reload":
                    return Dev.DevCommand.RunReload(rest, output, error, workingDirectory);
                case var tool when DevtoolsCommand.Tools.Contains(tool):
                    return DevtoolsCommand.Run(tool, rest, output, error, workingDirectory, devtools ?? new RealDevtoolsTransport());
                default:
                    error.WriteLine($"spar: unknown command '{args[0]}'.");
                    error.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }
    }
}
