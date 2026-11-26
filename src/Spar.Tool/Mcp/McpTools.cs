using System.Globalization;
using System.Text.Json;

namespace Spar.Tool.Mcp;

/// <summary>
/// The MCP server's tools (decision "M2e's decide-first questions", items 1 and 2): the same commands as the CLI, each
/// turned into the command line <c>spar</c> would get, so both surfaces run one code path and differ only in how they
/// are reached.
/// </summary>
internal static class McpTools
{
    private const string Project = """
        "project": { "type": "string", "description": "The app's project file or folder, if not the server's working folder." }
        """;

    private const string Timeout = """
        "timeout": { "type": "integer", "minimum": 0, "description": "How long to wait, in milliseconds; unfinished is not failed." }
        """;

    private const string Target = """
        "target": {
          "type": "object",
          "description": "The element: a reference from tree (ref, such as e12), or one locator (role with an optional name, text, label, placeholder, alt, title, testId or css), with exact and nth.",
          "properties": {
            "ref": { "type": "string" },
            "role": { "type": "string" },
            "name": { "type": "string" },
            "text": { "type": "string" },
            "label": { "type": "string" },
            "placeholder": { "type": "string" },
            "alt": { "type": "string" },
            "title": { "type": "string" },
            "testId": { "type": "string" },
            "css": { "type": "string" },
            "exact": { "type": "boolean" },
            "nth": { "type": "integer", "minimum": 0 }
          }
        }
        """;

    /// <summary>Every tool: its name, what it does, and its arguments' schema.</summary>
    public static readonly IReadOnlyList<McpToolDefinition> All =
    [
        Tool("tree", "The page's elements of the app's running Development build, each with a reference (e12) for click, fill and wait.",
            """ "limit": { "type": "integer", "minimum": 0, "description": "At most this many elements." } """),
        Tool("screenshot", "The page of the running app as a PNG image; also saved under the project's obj/spar-devtools/."),
        Tool("click", "Click an element of the page, then list the calls to the backend made while it ran.", Target, required: ["target"]),
        Tool("fill", "Replace the text of a field of the page.", Target + """, "text": { "type": "string" } """, required: ["target", "text"]),
        Tool("wait", "Wait until an element is visible (or hidden, attached, detached).",
            Target + """, "state": { "type": "string", "enum": ["visible", "hidden", "attached", "detached"] } """, required: ["target"]),
        Tool("contracts", "The app's commands (contract.command) with their requests, results and errors, and its contract snapshot."),
        Tool("call", "Run one of the app's commands as the page would, and show its outcome.",
            """
            "command": { "type": "string", "description": "contract.command, as contracts lists it." },
            "request": { "description": "The command's request, as JSON, when it takes one." }
            """, required: ["command"]),
        Tool("calls", "The last calls to the backend, from the page, tests and these tools, with the UI action behind each.",
            """
            "limit": { "type": "integer", "minimum": 0 },
            "since": { "type": "integer", "minimum": 0, "description": "Only calls after this sequence number." }
            """),
        Tool("logs", "The backend's log, the page's console and errors, and CSP violations, merged.",
            """
            "since": { "type": "string", "description": "A duration (30s, 5m) or a time." },
            "source": { "type": "string", "enum": ["backend", "page", "csp"] },
            "level": { "type": "string", "description": "The lowest level shown, such as warning." },
            "limit": { "type": "integer", "minimum": 0 }
            """),
        Tool("version", "The running app's run, build and host."),
        Tool("reload", "Ask the project's running spar dev to build again and restart the app: the build's errors and warnings at file and line, or the new run once its page is ready.",
            devtools: false),
        Tool("snap", "Capture scenes of the app's end-to-end tests: a PNG, the accessibility tree and the calls of each, in obj/spar-snap/.",
            """
            "scenes": { "type": "array", "items": { "type": "string" }, "description": "The scenes to capture." },
            "all": { "type": "boolean", "description": "Capture every scene." },
            "list": { "type": "boolean", "description": "List the scenes instead." },
            "noBuild": { "type": "boolean", "description": "Use the end-to-end project's last Debug build." }
            """, devtools: false),
        Tool("doctor", "Check the machine, the Spar app's project and the build's identity, with the fix for each finding.",
            """ "verbose": { "type": "boolean", "description": "Every check, not only what needs attention." } """, devtools: false),
    ];

    /// <summary>
    /// The command line for <paramref name="tool"/> with <paramref name="arguments"/>, as the CLI takes it; a wrong
    /// argument throws <see cref="McpArgumentException"/>.
    /// </summary>
    public static List<string> CommandLine(string tool, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var line = new List<string> { tool };
        foreach (var (name, value) in arguments)
        {
            switch (tool, name)
            {
                case (_, "project"):
                    line.AddRange(["--project", Text(name, value)]);
                    break;
                case (_, "timeout") when Devtools.DevtoolsCommand.Tools.Contains(tool):
                    line.AddRange(["--timeout", Count(name, value)]);
                    break;
                case ("click" or "fill" or "wait", "target"):
                    AddTarget(line, value);
                    break;
                case ("fill", "text"):
                    break;
                case ("wait", "state"):
                    line.AddRange(["--state", Text(name, value)]);
                    break;
                case ("call", "command"):
                    line.Insert(1, Text(name, value));
                    break;
                case ("call", "request"):
                    break;
                case ("tree" or "calls" or "logs", "limit") or ("calls", "since"):
                    line.AddRange(["--" + name, Count(name, value)]);
                    break;
                case ("logs", "since" or "source" or "level"):
                    line.AddRange(["--" + name, Text(name, value)]);
                    break;
                case ("snap", "scenes"):
                    line.AddRange(Texts(name, value));
                    break;
                case ("snap", "all" or "list") or ("doctor", "verbose"):
                    if (Flag(name, value))
                    {
                        line.Add("--" + name);
                    }

                    break;
                case ("snap", "noBuild"):
                    if (Flag(name, value))
                    {
                        line.Add("--no-build");
                    }

                    break;
                default:
                    throw new McpArgumentException($"{tool} takes no argument '{name}'.");
            }
        }

        // Positional values go after the options' target, in the order the CLI reads them.
        if (tool == "fill")
        {
            line.Add(arguments.TryGetValue("text", out var text) ? Text("text", text) : throw new McpArgumentException("fill needs text."));
        }

        if (tool == "call" && arguments.TryGetValue("request", out var request))
        {
            line.Add(request.GetRawText());
        }

        if (tool is "screenshot" or "doctor")
        {
            line.Add("--json");
        }

        return line;
    }

    private static McpToolDefinition Tool(string name, string description, string properties = "", string[]? required = null, bool devtools = true)
    {
        var shared = devtools ? Project + ",\n" + Timeout : Project;
        var all = properties.Trim().Length == 0 ? shared : properties.Trim() + ",\n" + shared;
        var requiredList = required is null ? "" : $", \"required\": [{string.Join(", ", required.Select(item => $"\"{item}\""))}]";
        using var schema = JsonDocument.Parse($$"""{ "type": "object", "properties": { {{all}} }{{requiredList}} }""");
        return new McpToolDefinition(name, description, schema.RootElement.Clone());
    }

    private static void AddTarget(List<string> line, JsonElement target)
    {
        if (target.ValueKind != JsonValueKind.Object)
        {
            throw new McpArgumentException("target must be an object: { \"ref\": \"e12\" } or a locator such as { \"role\": \"button\", \"name\": \"Save\" }.");
        }

        string? reference = null;
        foreach (var property in target.EnumerateObject())
        {
            switch (property.Name)
            {
                case "ref":
                    reference = Text("target.ref", property.Value);
                    break;
                case "exact":
                    if (Flag("target.exact", property.Value))
                    {
                        line.Add("--exact");
                    }

                    break;
                case "nth":
                    line.AddRange(["--nth", Count("target.nth", property.Value)]);
                    break;
                case "role" or "name" or "text" or "label" or "placeholder" or "alt" or "title" or "css":
                    line.AddRange(["--" + property.Name, Text("target." + property.Name, property.Value)]);
                    break;
                case "testId":
                    line.AddRange(["--testid", Text("target.testId", property.Value)]);
                    break;
                default:
                    throw new McpArgumentException($"target has no '{property.Name}'.");
            }
        }

        if (reference is not null)
        {
            line.Insert(1, reference);
        }
    }

    private static string Text(string name, JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new McpArgumentException($"{name} must be a string.");

    private static IEnumerable<string> Texts(string name, JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => Text(name, item)).ToList()
            : throw new McpArgumentException($"{name} must be an array of strings.");

    private static bool Flag(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new McpArgumentException($"{name} must be true or false."),
    };

    private static string Count(string name, JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0
            ? number.ToString(CultureInfo.InvariantCulture)
            : throw new McpArgumentException($"{name} must be a whole number of 0 or more.");
}

/// <summary>One tool of the MCP server.</summary>
internal sealed record McpToolDefinition(string Name, string Description, JsonElement InputSchema);

/// <summary>A tool's argument the CLI would not take.</summary>
internal sealed class McpArgumentException(string message) : Exception(message);
