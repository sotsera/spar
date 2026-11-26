using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Spar.Tool.Mcp;

namespace Spar.Tool.Tests;

/// <summary>
/// <c>spar mcp</c> (decision "M2e's decide-first questions", items 1 and 2): each tool becomes the command line the CLI
/// takes and runs the CLI's own command, so both surfaces send the app the same requests; the server speaks MCP over
/// stdio.
/// </summary>
public sealed class McpCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Every_cli_command_of_the_devtools_and_the_session_is_a_tool()
    {
        var names = McpTools.All.Select(tool => tool.Name).ToList();

        Assert.Equal([.. Devtools.DevtoolsCommand.Tools, "reload", "snap", "doctor"], names);
        foreach (var tool in McpTools.All)
        {
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            Assert.True(tool.InputSchema.GetProperty("properties").TryGetProperty("project", out _), tool.Name);
        }
    }

    [Theory]
    [InlineData("""{"target":{"ref":"e12"}}""", "click e12")]
    [InlineData("""{"target":{"role":"button","name":"Save","exact":true,"nth":1}}""", "click --role button --name Save --exact --nth 1")]
    [InlineData("""{"target":{"testId":"title"},"timeout":500}""", "click --testid title --timeout 500")]
    public void A_target_becomes_the_clis_reference_or_locator(string arguments, string expected)
    {
        Assert.Equal(expected, string.Join(' ', McpTools.CommandLine("click", Arguments(arguments))));
    }

    [Fact]
    public void Positional_values_go_where_the_cli_reads_them()
    {
        Assert.Equal(["fill", "e3", "new text"], McpTools.CommandLine("fill", Arguments("""{"text":"new text","target":{"ref":"e3"}}""")));
        Assert.Equal(["call", "pages.get", """{"id":"a"}"""], McpTools.CommandLine("call", Arguments("""{"request":{"id":"a"},"command":"pages.get"}""")));
        Assert.Equal(["snap", "home", "empty", "--no-build"], McpTools.CommandLine("snap", Arguments("""{"scenes":["home","empty"],"noBuild":true}""")));
        Assert.Equal(["doctor", "--verbose", "--json"], McpTools.CommandLine("doctor", Arguments("""{"verbose":true}""")));
        Assert.Equal(["screenshot", "--json"], McpTools.CommandLine("screenshot", Arguments("{}")));
    }

    [Theory]
    [InlineData("click", """{"target":"e12"}""", "target must be an object")]
    [InlineData("click", """{"target":{"ref":"e1","id":"x"}}""", "target has no 'id'")]
    [InlineData("tree", """{"limit":-1}""", "limit must be a whole number")]
    [InlineData("reload", """{"timeout":5}""", "reload takes no argument 'timeout'")]
    [InlineData("fill", """{"target":{"ref":"e1"}}""", "fill needs text")]
    public void A_wrong_argument_is_refused_before_anything_runs(string tool, string arguments, string message)
    {
        var refused = Assert.Throws<McpArgumentException>(() => McpTools.CommandLine(tool, Arguments(arguments)));

        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_call_sends_the_app_what_the_cli_sends_and_returns_its_text()
    {
        using var app = DevtoolsCommandTests.Listening(out _);
        var scripted = new DevtoolsCommandTests.ScriptedApp()
            .Reply("click", """{"element":"button \"Save\"","calls":[{"seq":4,"command":"pages.save","outcome":"success","ms":3.5}]}""");

        var result = await McpCommand.CallToolAsync(
            Call("click", """{"target":{"role":"button","name":"Save"}}"""), "0.1.0-dev.7", app.Path, Token, scripted);

        Assert.False(result.IsError);
        var request = Assert.Single(scripted.Requests, request => request.GetProperty("method").GetString() == "click");
        Assert.Equal("""{"role":"button","name":"Save"}""", request.GetProperty("params").GetProperty("target").GetRawText());
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain('\r', text);
        Assert.Contains("pages.save", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_that_fails_is_an_error_result_with_the_clis_message()
    {
        using var app = TemplateApp.New();

        var noRun = await McpCommand.CallToolAsync(Call("tree", "{}"), "0.1.0-dev.7", app.Path, Token);
        var unknown = await McpCommand.CallToolAsync(Call("open", "{}"), "0.1.0-dev.7", app.Path, Token);

        Assert.True(noRun.IsError);
        Assert.Contains("No Development run of this app is listening", Assert.IsType<TextContentBlock>(Assert.Single(noRun.Content)).Text, StringComparison.Ordinal);
        Assert.True(unknown.IsError);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task The_server_answers_mcp_over_stdio()
    {
        var tool = Path.Combine(AppContext.BaseDirectory, "Spar.Tool.dll");
        var start = new ProcessStartInfo("dotnet", [tool, "mcp"])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            KillOnParentExit = true,
        };
        using var server = Process.Start(start)!;
        try
        {
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
            using var initialized = JsonDocument.Parse(await ReadLineAsync(server));
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            using var listed = JsonDocument.Parse(await ReadLineAsync(server));

            var info = initialized.RootElement.GetProperty("result").GetProperty("serverInfo");
            Assert.Equal("spar", info.GetProperty("name").GetString());
            var tools = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(item => item.GetProperty("name").GetString());
            Assert.Equal(McpTools.All.Select(item => item.Name), tools);
        }
        finally
        {
            server.StandardInput.Close();
            if (!server.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                server.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<string> ReadLineAsync(Process server)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        return await server.StandardOutput.ReadLineAsync(limit.Token) ?? throw new InvalidOperationException("The server closed its output.");
    }

    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static CallToolRequestParams Call(string name, string arguments) => new() { Name = name, Arguments = Arguments(arguments) };
}
