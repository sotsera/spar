using System.Globalization;
using System.Text.Json;
using Spar.Tool.Project;

namespace Spar.Tool.Devtools;

/// <summary>
/// The devtools' commands (decision "M2a's decide-first questions", item 7): <c>spar tree</c>, <c>screenshot</c>,
/// <c>click</c>, <c>fill</c>, <c>wait</c>, <c>contracts</c>, <c>call</c>, <c>calls</c>, <c>logs</c> and <c>version</c>,
/// each one request to the Development run of the app in the current folder (or <c>--project</c>), found through its
/// <c>obj/spar-devtools.json</c>. Text by default, <c>--json</c> for the result as the app sent it; <c>spar doctor</c>'s
/// exit codes: 0 done, 2 the app refused or no run is listening, 64 a wrong command line, 70 the tool failed.
/// </summary>
internal static class DevtoolsCommand
{
    public static readonly string[] Tools = ["tree", "screenshot", "click", "fill", "wait", "contracts", "call", "calls", "logs", "version"];

    public const string Usage = """
        Devtools of a running Development build (the app in the current folder, or --project):
          tree [--limit N]                    The page's elements, each with a reference (e12) for the commands below.
          screenshot [--out file]             The page as PNG (default: obj/spar-devtools/screenshot-<time>.png).
          click <target>                      Click an element, then list the calls it caused.
          fill <target> <text>                Replace a field's text.
          wait <target> [--state S]           Wait until the element is visible (or hidden, attached, detached).
          contracts                           The app's commands and its contract snapshot.
          call <contract.command> [json]      Run a command, as the page would, and show its outcome.
          calls [--limit N] [--since seq]     The last calls, from the page, tests and these tools.
          logs [--since 30s] [--source S] [--level L] [--limit N]
                                              The backend log, the page's console and errors, CSP violations.
          version                             The run, the build and the host.

        A target is a reference from tree (e12), or a locator: --role R [--name N], --text T, --label L,
        --placeholder P, --alt A, --title T, --testid ID or --css SELECTOR, with --exact and --nth N.

        Options: --project <path>, --json (the result as JSON), --timeout <ms> (how long to wait; unfinished is
        not failed). Exit codes: 0 done, 2 the app refused or no run is listening, 64 a wrong command line.
        """;

    public static int Run(string tool, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, string workingDirectory, IDevtoolsTransport transport)
    {
        Options options;
        try
        {
            options = Options.Parse(tool, arguments);
        }
        catch (UsageException usage)
        {
            error.WriteLine($"spar {tool}: {usage.Message}");
            error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        if (options.Help)
        {
            output.WriteLine(Usage);
            return ExitCodes.Success;
        }

        var project = AppProject.Find(Path.Combine(workingDirectory, options.Project ?? "."), out var problem);
        if (project is null)
        {
            error.WriteLine($"spar {tool}: {problem}");
            return ExitCodes.Errors;
        }

        if (DevtoolsTarget.Find(project.Directory, out var noRun) is not { } target)
        {
            error.WriteLine($"spar {tool}: {noRun}");
            return ExitCodes.Errors;
        }

        try
        {
            return RunAsync(tool, options, target, project.Directory, output, transport).GetAwaiter().GetResult();
        }
        catch (DevtoolsCallException refused)
        {
            if (options.Json)
            {
                output.WriteLine(JsonText.Indented(refused.Error));
            }

            error.WriteLine($"spar {tool}: {refused.Message}{(refused.Reason is { Length: > 0 } reason ? $" ({reason})" : "")}");
            DevtoolsOutput.Matches(error, refused.Error);
            return ExitCodes.Errors;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            error.WriteLine($"spar {tool}: the app's devtools did not answer ({exception.Message}).");
            return ExitCodes.Errors;
        }
    }

    private static async Task<int> RunAsync(string tool, Options options, DevtoolsTarget target, string projectDirectory, TextWriter output, IDevtoolsTransport transport)
    {
        // A tool that waits gets its own limit plus a margin; the connection's own is a minute otherwise.
        using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds((options.Timeout ?? 50_000) + 10_000));
        await using var connection = await DevtoolsConnection.OpenAsync(transport, target, limit.Token);
        using var response = await connection.RequestAsync(tool, options.WriteParameters, limit.Token);
        var root = response.RootElement;
        if (root.TryGetProperty("error", out var refused))
        {
            throw new DevtoolsCallException(refused.Clone());
        }

        var result = root.GetProperty("result");
        if (tool == "screenshot")
        {
            return DevtoolsOutput.Screenshot(output, result, options.Out, projectDirectory, options.Json);
        }

        if (options.Json)
        {
            output.WriteLine(JsonText.Indented(result));
            return ExitCodes.Success;
        }

        DevtoolsOutput.Write(output, tool, result);
        return ExitCodes.Success;
    }

    private sealed class UsageException(string message) : Exception(message);

    /// <summary>One command line, parsed for its tool.</summary>
    private sealed class Options
    {
        private readonly Dictionary<string, string> _target = new(StringComparer.Ordinal);
        private readonly List<string> _positional = [];
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private string _tool = "";

        public bool Help { get; private set; }

        public bool Json { get; private set; }

        public bool Exact { get; private set; }

        public string? Project { get; private set; }

        public string? Out { get; private set; }

        public int? Timeout { get; private set; }

        public static Options Parse(string tool, IReadOnlyList<string> arguments)
        {
            var options = new Options { _tool = tool };
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                string Next() => index + 1 < arguments.Count ? arguments[++index] : throw new UsageException($"{argument} needs a value.");
                switch (argument)
                {
                    case "-h" or "--help" or "-?":
                        options.Help = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    case "--exact":
                        options.Exact = true;
                        break;
                    case "--project":
                        options.Project = Next();
                        break;
                    case "--out" when tool == "screenshot":
                        options.Out = Next();
                        break;
                    case "--timeout":
                        options.Timeout = Integer(argument, Next());
                        break;
                    case "--role" or "--name" or "--text" or "--label" or "--placeholder" or "--alt" or "--title" or "--testid" or "--css" or "--nth":
                        options._target[argument[2..]] = Next();
                        break;
                    case "--limit" or "--since" or "--source" or "--level" or "--state":
                        var value = Next();

                        // A limit is a count, and so is calls' since (a sequence number); logs' since is a duration or a time.
                        if (argument == "--limit" || (argument == "--since" && tool == "calls"))
                        {
                            Integer(argument, value);
                        }

                        options._values[argument[2..]] = value;
                        break;
                    default:
                        if (argument.StartsWith('-') && argument.Length > 1)
                        {
                            throw new UsageException($"unknown option '{argument}'.");
                        }

                        options._positional.Add(argument);
                        break;
                }
            }

            if (!options.Help)
            {
                options.Check();
            }

            return options;
        }

        /// <summary>The request's parameters: the target, the tool's values, its text or command, and the timeout.</summary>
        public void WriteParameters(Utf8JsonWriter writer)
        {
            if (Timeout is { } timeout)
            {
                writer.WriteNumber("timeout", timeout);
            }

            switch (_tool)
            {
                case "click" or "fill" or "wait":
                    WriteTarget(writer);
                    if (_tool == "fill")
                    {
                        writer.WriteString("text", _positional[^1]);
                    }

                    break;
                case "call":
                    writer.WriteString("command", _positional[0]);
                    if (_positional.Count > 1)
                    {
                        writer.WritePropertyName("request");
                        using var request = JsonDocument.Parse(_positional[1]);
                        request.RootElement.WriteTo(writer);
                    }

                    break;
            }

            foreach (var (name, value) in _values)
            {
                if (name == "limit" || (name == "since" && _tool == "calls"))
                {
                    writer.WriteNumber(name, Integer("--" + name, value));
                }
                else
                {
                    writer.WriteString(name, value);
                }
            }
        }

        private void WriteTarget(Utf8JsonWriter writer)
        {
            writer.WriteStartObject("target");
            if (_target.Count == 0)
            {
                writer.WriteString("ref", _positional[0]);
            }

            foreach (var (name, value) in _target)
            {
                if (name == "nth")
                {
                    writer.WriteNumber("nth", Integer("--nth", value));
                }
                else
                {
                    writer.WriteString(name == "testid" ? "testId" : name, value);
                }
            }

            if (Exact)
            {
                writer.WriteBoolean("exact", true);
            }

            writer.WriteEndObject();
        }

        private void Check()
        {
            var locators = _target.Keys.Count(key => key is not ("nth" or "name"));
            switch (_tool)
            {
                case "click" or "wait" or "fill":
                    var wantsText = _tool == "fill" ? 1 : 0;
                    var reference = locators == 0 ? 1 : 0;
                    if (locators > 1)
                    {
                        throw new UsageException("name the element once: a reference, or one locator (--role with --name counts as one).");
                    }

                    if (_positional.Count != reference + wantsText)
                    {
                        throw new UsageException(_tool == "fill"
                            ? "give the element (e12, or a locator) and the text: spar fill e12 \"new text\"."
                            : "give the element: a reference from spar tree (e12), or a locator such as --role button --name Save.");
                    }

                    if (reference == 1 && !IsReference(_positional[0]))
                    {
                        throw new UsageException($"'{_positional[0]}' is not a reference from spar tree (e1, e2, …); use a locator option instead.");
                    }

                    if (_target.ContainsKey("name") && !_target.ContainsKey("role"))
                    {
                        throw new UsageException("--name goes with --role.");
                    }

                    break;
                case "call":
                    if (_positional.Count is < 1 or > 2)
                    {
                        throw new UsageException("give the command and, if it takes one, its request as JSON: spar call pages.get '{\"id\":\"a\"}'.");
                    }

                    if (_positional.Count == 2)
                    {
                        try
                        {
                            using var _ = JsonDocument.Parse(_positional[1]);
                        }
                        catch (JsonException exception)
                        {
                            throw new UsageException($"the request is not JSON: {exception.Message}");
                        }
                    }

                    break;
                default:
                    if (_positional.Count > 0 || _target.Count > 0)
                    {
                        throw new UsageException($"takes no element or argument ('{(_positional.Count > 0 ? _positional[0] : "--" + _target.Keys.First())}').");
                    }

                    break;
            }
        }

        private static bool IsReference(string text) =>
            text.Length > 1 && text[0] == 'e' && text.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

        private static int Integer(string option, string value) =>
            int.TryParse(value, CultureInfo.InvariantCulture, out var number) && number >= 0
                ? number
                : throw new UsageException($"{option} is '{value}', not a whole number.");
    }
}
