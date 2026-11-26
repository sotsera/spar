using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Spar.Tool.Mcp;

/// <summary>
/// <c>spar mcp</c> (plan, M2e; decision "M2e's decide-first questions", items 1 and 2): an MCP server over stdio with the
/// CLI's commands as its tools. Each call runs the same command the CLI would, in this process, with its output
/// captured: the text it prints is the tool's text, a screenshot is also an image, and an exit code that means the
/// command failed marks the result as an error. The client starts it in the app's folder.
/// </summary>
internal static class McpCommand
{
    public const string Usage = """
        Usage: spar mcp

        Serves the spar commands as MCP tools over stdin and stdout, for an agent's MCP client: tree, screenshot, click,
        fill, wait, contracts, call, calls, logs and version (the devtools of the running Development build), reload
        (spar dev's), snap and doctor. Start spar dev first for the devtools and reload. The tools work on the app in the
        folder the client starts this server in, or the project each call names.
        """;

    private const string Instructions = """
        Tools for a Spar desktop app (a .NET backend and a web page in a native window). Start the app with
        `dotnet spar dev` in the app's folder, in the background, before using the devtools (tree, screenshot, click,
        fill, wait, contracts, call, calls, logs, version) or reload. reload rebuilds and restarts the app, and answers
        with the build's errors at file and line. snap captures the end-to-end tests' scenes; doctor checks the machine
        and the project.
        """;

    public static int Run(string[] args, string version, TextWriter output, TextWriter error, string workingDirectory)
    {
        if (args.Length > 0)
        {
            if (args[0] is "-h" or "--help" or "-?")
            {
                output.WriteLine(Usage);
                return ExitCodes.Success;
            }

            error.WriteLine($"spar mcp: unknown option '{args[0]}'.");
            error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "spar", Version = version },
            ServerInstructions = Instructions,
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(ListTools()),
                CallToolHandler = (request, cancellationToken) => CallToolAsync(request.Params, version, workingDirectory, cancellationToken),
            },
        };

        RunAsync(options).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    /// <summary>One tool call: the CLI's command, run on a pool thread so the server keeps reading.</summary>
    internal static async ValueTask<CallToolResult> CallToolAsync(
        CallToolRequestParams? call, string version, string workingDirectory, CancellationToken cancellationToken, Devtools.IDevtoolsTransport? devtools = null)
    {
        if (call is null || McpTools.All.All(tool => tool.Name != call.Name))
        {
            return Failure($"No tool '{call?.Name}'.");
        }

        List<string> line;
        try
        {
            var arguments = call.Arguments is { } given ? new Dictionary<string, JsonElement>(given) : [];
            line = McpTools.CommandLine(call.Name, arguments);
        }
        catch (McpArgumentException wrong)
        {
            return Failure(wrong.Message);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await Task.Run(
            () => Cli.Run([.. line], version, output, error, TextReader.Null, interactive: false, new Machine.RealMachine(), new Init.RealNpm(), workingDirectory, devtools),
            cancellationToken);

        return Result(call.Name, exitCode, output.ToString(), error.ToString());
    }

    private static async Task RunAsync(McpServerOptions options)
    {
        await using var transport = new StdioServerTransport("spar");
        await using var server = McpServer.Create(transport, options);
        await server.RunAsync();
    }

    private static ListToolsResult ListTools() => new()
    {
        Tools = [.. McpTools.All.Select(tool => new ModelContextProtocol.Protocol.Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema,
        })],
    };

    /// <summary>The command's outcome: its text, a screenshot's image, and whether the command failed.</summary>
    private static CallToolResult Result(string tool, int exitCode, string output, string error)
    {
        var failed = exitCode is not (ExitCodes.Success or ExitCodes.Warnings);
        var content = new List<ContentBlock>();
        var text = output.ReplaceLineEndings("\n").TrimEnd();
        if (tool == "screenshot" && !failed && Screenshot(text) is { } image)
        {
            content.Add(ImageContentBlock.FromBytes(File.ReadAllBytes(image.Path), "image/png"));
            text = $"{image.Path} ({image.Size})";
        }

        if (error.ReplaceLineEndings("\n").Trim() is { Length: > 0 } problems)
        {
            text = text.Length == 0 ? problems : $"{text}\n{problems}";
        }

        content.Add(new TextContentBlock { Text = text.Length == 0 ? $"Done (exit code {exitCode})." : text });
        return new CallToolResult { Content = content, IsError = failed };
    }

    /// <summary>The file and size the screenshot command wrote, from its JSON line.</summary>
    private static (string Path, string Size)? Screenshot(string json)
    {
        try
        {
            using var written = JsonDocument.Parse(json);
            var root = written.RootElement;
            return (root.GetProperty("path").GetString()!, $"{root.GetProperty("width")}x{root.GetProperty("height")}");
        }
        catch (Exception problem) when (problem is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static CallToolResult Failure(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true,
    };
}
