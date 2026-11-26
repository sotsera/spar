using System.Text;
using System.Text.Json;
using Spar.Tool.Devtools;
using Spar.Tool.Init;

namespace Spar.Tool.Tests;

/// <summary>
/// The devtools' commands of the spar tool against a scripted app (decision "M2a's decide-first questions", item 7):
/// finding the run, the command lines, the requests they send, and their text and JSON output.
/// </summary>
public sealed class DevtoolsCommandTests
{
    [Fact]
    public void Without_a_listening_run_the_command_says_how_to_start_one()
    {
        using var app = TemplateApp.New();
        var (missing, _, noFile) = Spar(app, new ScriptedApp(), "tree");
        app.Write("obj/spar-devtools.json", Discovery(processId: int.MaxValue));
        var (ended, _, gone) = Spar(app, new ScriptedApp(), "tree");

        Assert.Equal((ExitCodes.Errors, ExitCodes.Errors), (missing, ended));
        Assert.Contains("No Development run of this app is listening", noFile, StringComparison.Ordinal);
        Assert.Contains("has ended; start the app again", gone, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fill", "e3")]
    [InlineData("click", "save")]
    [InlineData("click", "--role", "button", "--text", "Save")]
    [InlineData("click", "--name", "Save")]
    [InlineData("call", "pages.get", "{not json")]
    [InlineData("tree", "e1")]
    [InlineData("calls", "--limit", "many")]
    [InlineData("logs", "--color")]
    public void A_wrong_command_line_is_exit_code_64(params string[] arguments)
    {
        using var app = Listening(out _);

        var (exit, _, error) = Spar(app, new ScriptedApp(), arguments);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains($"spar {arguments[0]}:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_request_follows_initialize_with_the_token_and_names_the_run()
    {
        using var app = Listening(out var token);
        var scripted = new ScriptedApp().Reply("click", """{"element":"button \"Save\"","calls":[]}""");

        Spar(app, scripted, "click", "--role", "button", "--name", "Save", "--exact", "--nth", "1", "--timeout", "2000");

        var (initialize, click) = (scripted.Requests[0], scripted.Requests[1]);
        Assert.Equal(("initialize", token), (initialize.GetProperty("method").GetString(), initialize.GetProperty("params").GetProperty("token").GetString()));
        var parameters = click.GetProperty("params");
        Assert.Equal("c0ffee01", parameters.GetProperty("run").GetString());
        Assert.Equal(2000, parameters.GetProperty("timeout").GetInt32());
        Assert.Equal("""{"role":"button","name":"Save","nth":1,"exact":true}""", Element(click));
    }

    [Fact]
    public void Tree_prints_each_element_with_its_reference_depth_and_states()
    {
        using var app = Listening(out _);
        var scripted = new ScriptedApp().Reply("tree", """
            {"document":2,"title":"Notebook","url":"https://app.localhost/","truncated":true,"nodes":[
              {"ref":"e1","role":"main","name":"","depth":0},
              {"ref":"e2","role":"heading","name":"Ideas","depth":1,"level":2},
              {"ref":"e3","role":"textbox","name":"Title","depth":1,"value":"Ideas\nand more","focused":true},
              {"ref":"e4","role":"checkbox","name":"Done","depth":1,"checked":false,"disabled":true}]}
            """);

        var (exit, output, _) = Spar(app, scripted, "tree");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            Notebook — https://app.localhost/ (document 2)
            e1 main
              e2 heading "Ideas" [level 2]
              e3 textbox "Title" = "Ideas\nand more" [focused]
              e4 checkbox "Done" [not checked, disabled]
            … more elements than the limit; spar tree --limit N shows more.

            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void An_action_lists_the_calls_it_caused_and_says_when_its_input_was_synthetic()
    {
        using var app = Listening(out _);
        var scripted = new ScriptedApp().Reply("click", """
            {"element":"button \"Create page\"","synthetic":true,"calls":[
              {"sequence":3,"callId":7,"command":"pages.create","caller":"page","outcome":"success","milliseconds":4.8},
              {"sequence":4,"callId":8,"command":"pages.list","caller":"page","outcome":"error","code":"notFound","milliseconds":5.1}]}
            """);

        var (_, output, _) = Spar(app, scripted, "click", "e17");

        Assert.Equal(
            """
            clicked button "Create page" (the page's own events: no trusted input on this desktop)
            calls while it ran (2):
              #3 page     pages.create success 4.8 ms
              #4 page     pages.list error notFound 5.1 ms

            """.ReplaceLineEndings("\n"), output);
        Assert.Equal("""{"ref":"e17"}""", Element(scripted.Requests[1]));
    }

    [Fact]
    public void Call_sends_the_request_json_and_json_prints_the_result_as_sent()
    {
        using var app = Listening(out _);
        var scripted = new ScriptedApp().Reply("call", """{"command":"pages.get","callId":1,"outcome":"success","value":{"id":"a"}}""");

        var (exit, output, _) = Spar(app, scripted, "call", "pages.get", """{"id":"a"}""", "--json");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("""{"id":"a"}""", scripted.Requests[1].GetProperty("params").GetProperty("request").GetRawText());
        Assert.Equal("a", JsonDocument.Parse(output).RootElement.GetProperty("value").GetProperty("id").GetString());
    }

    [Fact]
    public void A_refusal_is_exit_code_2_with_its_reason_and_the_matches()
    {
        using var app = Listening(out _);
        var scripted = new ScriptedApp().Refuse("click", "ambiguous", "The target matches 2 elements.", """["button \"A\"","button \"B\""]""");

        var (exit, _, error) = Spar(app, scripted, "click", "--role", "button");

        Assert.Equal(ExitCodes.Errors, exit);
        Assert.Equal(
            """
            spar click: The target matches 2 elements. (ambiguous)
              button "A"
              button "B"

            """.ReplaceLineEndings("\n"), error);
    }

    [Fact]
    public void Screenshot_writes_the_png_and_prints_its_path()
    {
        using var app = Listening(out _);
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        var scripted = new ScriptedApp().Reply("screenshot", $$"""{"format":"png","width":3,"height":2,"bytes":7,"data":"{{Convert.ToBase64String(png)}}"}""");
        var file = Path.Combine(app.Path, "shots", "page.png");

        var (exit, output, _) = Spar(app, scripted, "screenshot", "--out", file);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(png, File.ReadAllBytes(file));
        Assert.Equal($"{file} (3x2, 7 bytes)\n", output);
    }

    internal static TestFolder Listening(out string token)
    {
        var app = TemplateApp.New();
        token = "5ecret";
        app.Write("obj/spar-devtools.json", Discovery(Environment.ProcessId, token));
        return app;
    }

    private static string Discovery(int processId, string token = "5ecret") =>
        $$"""{"version":1,"pid":{{processId}},"run":"c0ffee01","host":"windows","transport":"pipe","address":"spar-devtools-test","token":"{{token}}","app":"com.example.MyApp"}""";

    /// <summary>The element a request names, as the tool wrote it.</summary>
    private static string Element(JsonElement request) => request.GetProperty("params").GetProperty("target").GetRawText();

    private static (int Exit, string Output, string Error) Spar(TestFolder app, ScriptedApp scripted, params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Tool.Cli.Run(arguments, "0.1.0-dev.7", output, error, new StringReader(""), false, new FakeMachine(), new RealNpm(), app.Path, scripted);
        return (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>An app's devtools that answer <c>initialize</c>, then each tool from a script.</summary>
    internal sealed class ScriptedApp : IDevtoolsTransport
    {
        private readonly Dictionary<string, string> _replies = new(StringComparer.Ordinal);

        public List<JsonElement> Requests { get; } = [];

        public ScriptedApp Reply(string method, string result)
        {
            _replies[method] = "{\"result\":" + Compact(result) + "}";
            return this;
        }

        public ScriptedApp Refuse(string method, string reason, string message, string matches) =>
            Store(method, "{\"error\":{\"code\":-32000,\"message\":\"" + message + "\",\"data\":{\"reason\":\"" + reason + "\",\"matches\":" + matches + "}}}");

        public Task<Stream> OpenAsync(DevtoolsTarget target, CancellationToken cancellationToken) => Task.FromResult<Stream>(new Conversation(this));

        /// <summary>One line of JSON, as the app sends it.</summary>
        private static string Compact(string json)
        {
            using var document = JsonDocument.Parse(json);
            var output = new System.Buffers.ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(output))
            {
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(output.WrittenSpan);
        }

        private ScriptedApp Store(string method, string body)
        {
            _replies[method] = body;
            return this;
        }

        private byte[] Answer(JsonElement request)
        {
            Requests.Add(request.Clone());
            var method = request.GetProperty("method").GetString()!;
            var body = method == "initialize"
                ? """{"result":{"protocol":1,"run":"c0ffee01"}}"""
                : _replies.GetValueOrDefault(method, """{"result":{}}""");
            var id = request.GetProperty("id").GetRawText();
            return Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":{{id}},{{body[1..^1]}}}""" + "\n");
        }

        /// <summary>The connection: what the tool writes is read line by line and answered at once.</summary>
        private sealed class Conversation(ScriptedApp app) : Stream
        {
            private readonly MemoryStream _written = new();
            private readonly Queue<byte> _answers = new();

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                foreach (var b in buffer.AsSpan(offset, count))
                {
                    if (b != '\n')
                    {
                        _written.WriteByte(b);
                        continue;
                    }

                    using var request = JsonDocument.Parse(_written.ToArray());
                    foreach (var answer in app.Answer(request.RootElement))
                    {
                        _answers.Enqueue(answer);
                    }

                    _written.SetLength(0);
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = 0;
                while (read < count && _answers.Count > 0)
                {
                    buffer[offset + read++] = _answers.Dequeue();
                }

                return read;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
