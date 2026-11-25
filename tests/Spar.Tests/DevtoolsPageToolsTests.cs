using System.Collections.Concurrent;
using System.Text.Json;
using Spar.Devtools;

namespace Spar.Tests;

/// <summary>
/// The devtools' page tools against a scripted page (decision "M2a's decide-first questions", items 4, 5 and 8): targets
/// as the engine's locator chains, references valid for one document, the trusted and the synthetic click, fill, wait,
/// ambiguity and timeouts, and the calls an action caused.
/// </summary>
public sealed class DevtoolsPageToolsTests : IAsyncLifetime
{
    private readonly DevtoolsPages _pages = new();
    private readonly DevtoolsLog _log = new(TimeProvider.System);
    private TestBackend _backend = null!;
    private DevtoolsTools _tools = null!;
    private int _id;

    public ValueTask InitializeAsync()
    {
        _backend = TestBackend.Create();
        var file = new DevtoolsFile(1, "run", DateTimeOffset.UtcNow, "windows", "pipe", "x", "token", "app", "");
        _tools = new DevtoolsTools(new DevtoolsContext(_backend.App, "run", "windows", "unused", _pages, _log), file);
        _backend.App.Dispatcher.Recorder = _tools.Calls;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _backend.DisposeAsync();

    [Fact]
    public async Task Without_a_page_the_tools_say_why()
    {
        var starting = await RunAsync("tree");
        var loopback = new DevtoolsTools(
            new DevtoolsContext(_backend.App, "run", "loopback", "unused", new DevtoolsPages { Unavailable = "The page runs in a browser." }, _log),
            new DevtoolsFile(1, "run", DateTimeOffset.UtcNow, "loopback", "pipe", "x", "token", "app", ""));
        var inBrowser = Parse(await loopback.RunAsync(Id(), "click", Params("""{"target":{"ref":"e1"}}"""), CancellationToken.None));

        Assert.Equal("no_page", Reason(starting));
        Assert.Equal(("unavailable", "The page runs in a browser."), (Reason(inBrowser), Message(inBrowser)));
    }

    [Fact]
    public async Task A_reference_is_for_the_document_its_tree_came_from()
    {
        var page = Use(new ScriptedPage());
        page.Answer("snapshot", """{"nodes":[{"ref":"e1","role":"button","name":"Save","depth":0}],"truncated":false,"title":"T","url":"https://app.localhost/"}""");
        page.Answer("actionable", """{"ok":true,"element":"button \"Save\"","x":10,"y":20}""");
        page.Answer("landed", """{"armed":true,"seen":true,"onTarget":true}""");

        var before = await RunAsync("click", """{"target":{"ref":"e1"}}""");
        var tree = Result(await RunAsync("tree"));
        var clicked = await RunAsync("click", """{"target":{"ref":"e1"}}""");
        page.Document++;
        var after = await RunAsync("click", """{"target":{"ref":"e1"}}""");

        Assert.Equal("stale_target", Reason(before));
        Assert.Equal(1, tree.GetProperty("document").GetInt64());
        Assert.Equal("Save", tree.GetProperty("nodes")[0].GetProperty("name").GetString());
        Assert.Equal("button \"Save\"", Result(clicked).GetProperty("element").GetString());
        Assert.Equal("stale_target", Reason(after));
        Assert.Contains("take tree again", Message(after), StringComparison.Ordinal);
        Assert.Equal("""[{"kind":"ref","ref":"e1"}]""", page.Chains("actionable").Single());
    }

    [Fact]
    public async Task A_trusted_click_presses_at_the_point_tries_again_when_the_press_missed_and_lists_its_calls()
    {
        var page = Use(new ScriptedPage());
        page.Answer("actionable", """{"ok":true,"element":"button \"Save\"","x":10.5,"y":20}""");
        page.Answer("landed", """{"armed":true,"seen":false}""", """{"armed":true,"seen":true,"onTarget":true}""");
        page.OnClick = () => _ = _backend.Dispatcher.InvokeAsync("echo", "back", 1, "{}"u8.ToArray(), CancellationToken.None).AsTask();

        var clicked = Result(await RunAsync("click", """{"target":{"role":"button","name":"Save","exact":true}}"""));

        Assert.Equal([(10.5, 20.0), (10.5, 20.0)], page.Clicks);
        Assert.Equal("""[{"kind":"role","role":"button","name":"Save","exact":true}]""", page.Chains("actionable")[0]);
        Assert.False(clicked.TryGetProperty("synthetic", out _));
        Assert.Equal(["echo.back", "echo.back"], clicked.GetProperty("calls").EnumerateArray().Select(call => call.GetProperty("command").GetString()));
        Assert.Equal(1, page.Requests.Count(request => request.Op == "frames"));
    }

    [Fact]
    public async Task Without_trusted_input_the_click_is_the_engines_and_says_so()
    {
        var page = Use(new ScriptedPage { TrustedInput = false });
        page.Answer("syntheticClick", """{"ok":true,"element":"link \"Docs\"","x":1,"y":2,"synthetic":true}""");

        var clicked = Result(await RunAsync("click", """{"target":{"text":"Docs","nth":1}}"""));

        Assert.True(clicked.GetProperty("synthetic").GetBoolean());
        Assert.Empty(page.Clicks);
        Assert.Equal("""[{"kind":"text","text":"Docs"},{"kind":"nth","index":1}]""", page.Chains("syntheticClick").Single());
    }

    [Fact]
    public async Task Several_matches_are_ambiguous_with_the_elements_and_a_missing_one_runs_out_of_time()
    {
        var page = Use(new ScriptedPage());
        page.Answer("actionable", """{"strict":2,"matches":["button \"A\"","button \"B\""]}""");
        var ambiguous = await RunAsync("click", """{"target":{"role":"button"}}""");
        page.Answer("actionable", """{"retry":"no element matches","near":[]}""");
        var missing = await RunAsync("click", """{"target":{"testId":"save \"x\""},"timeout":300}""");

        Assert.Equal("ambiguous", Reason(ambiguous));
        Assert.Equal(["button \"A\"", "button \"B\""], ambiguous.GetProperty("error").GetProperty("data").GetProperty("matches").EnumerateArray().Select(match => match.GetString()));
        Assert.Equal("timeout", Reason(missing));
        Assert.Contains("no element matches", Message(missing), StringComparison.Ordinal);
        var selector = JsonDocument.Parse(page.Chains("actionable")[1]).RootElement[0].GetProperty("selector").GetString();
        Assert.Equal("""[data-testid="save \"x\""]""", selector);
    }

    [Fact]
    public async Task Fill_inserts_the_text_unless_the_engine_set_it_and_empty_text_deletes()
    {
        var page = Use(new ScriptedPage());
        page.Answer("prepareFill", """{"ok":true,"done":false,"element":"textbox \"Title\""}""");
        var typed = Result(await RunAsync("fill", """{"target":{"label":"Title"},"text":"Ideas"}"""));
        var emptied = Result(await RunAsync("fill", """{"target":{"label":"Title"},"text":""}"""));
        page.Answer("prepareFill", """{"ok":true,"done":true,"element":"textbox \"Date\""}""");
        await RunAsync("fill", """{"target":{"label":"Date"},"text":"2026-11-26"}""");
        var noText = await RunAsync("fill", """{"target":{"label":"Title"}}""");

        Assert.Equal("textbox \"Title\"", typed.GetProperty("element").GetString());
        Assert.Equal(["Ideas"], page.Inserted);
        Assert.Equal("textbox \"Title\"", emptied.GetProperty("element").GetString());
        Assert.Single(page.Requests, request => request.Op == "insertText" && request.Json.Contains("\"text\":\"\"", StringComparison.Ordinal));
        Assert.Equal("invalid", Reason(noText));
    }

    [Fact]
    public async Task Wait_returns_once_the_element_is_in_the_state_asked_for()
    {
        var page = Use(new ScriptedPage());
        page.Answer("state", """{"count":0}""", """{"count":1,"visible":false,"element":"dialog"}""", """{"count":1,"visible":true,"element":"dialog \"New page\""}""");
        var shown = Result(await RunAsync("wait", """{"target":{"role":"dialog"}}"""));
        page.Answer("state", """{"count":0}""");
        var hidden = Result(await RunAsync("wait", """{"target":{"role":"dialog"},"state":"hidden"}"""));
        page.Answer("state", """{"count":2,"matches":["heading \"A\"","heading \"A\""]}""");
        var ambiguous = await RunAsync("wait", """{"target":{"role":"heading"}}""");
        var wrong = await RunAsync("wait", """{"target":{"role":"dialog"},"state":"gone"}""");

        Assert.Equal(("visible", "dialog \"New page\""), (shown.GetProperty("state").GetString(), shown.GetProperty("element").GetString()));
        Assert.Equal("hidden", hidden.GetProperty("state").GetString());
        Assert.Equal("ambiguous", Reason(ambiguous));
        Assert.Equal("invalid", Reason(wrong));
    }

    [Fact]
    public async Task Screenshot_gives_the_png_and_its_size()
    {
        Use(new ScriptedPage());

        var shot = Result(await RunAsync("screenshot"));

        Assert.Equal((3, 2, "png"), (shot.GetProperty("width").GetInt32(), shot.GetProperty("height").GetInt32(), shot.GetProperty("format").GetString()));
        Assert.Equal(ScriptedPage.Png, shot.GetProperty("data").GetBytesFromBase64());
    }

    private ScriptedPage Use(ScriptedPage page)
    {
        _pages.Current = page;
        return page;
    }

    private async Task<JsonElement> RunAsync(string tool, string parameters = "{}") =>
        Parse(await _tools.RunAsync(Id(), tool, Params(parameters), CancellationToken.None));

    private JsonElement Id() => JsonDocument.Parse((++_id).ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement;

    private static JsonElement Params(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement Parse(byte[] response) => JsonDocument.Parse(response).RootElement;

    private static JsonElement Result(JsonElement response) =>
        response.TryGetProperty("result", out var result) ? result : throw new InvalidOperationException("No result: " + response);

    private static string? Reason(JsonElement response) =>
        response.TryGetProperty("error", out var error) && error.TryGetProperty("data", out var data) && data.TryGetProperty("reason", out var reason)
            ? reason.GetString()
            : null;

    private static string Message(JsonElement response) => response.GetProperty("error").GetProperty("message").GetString() ?? "";

    /// <summary>A page whose engine answers each operation from a script, the last answer repeating.</summary>
    private sealed class ScriptedPage : IDevtoolsPage
    {
        public static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 3, 0, 0, 0, 2];

        private readonly ConcurrentDictionary<string, Queue<string>> _answers = new();

        public bool TrustedInput { get; init; } = true;

        public long Document { get; set; } = 1;

        public Action? OnClick { get; set; }

        public ConcurrentQueue<(string Op, string Json)> Requests { get; } = new();

        public List<(double X, double Y)> Clicks { get; } = [];

        public List<string> Inserted { get; } = [];

        public void Answer(string op, params string[] answers) => _answers[op] = new Queue<string>(answers);

        public List<string> Chains(string op) =>
            [.. Requests.Where(request => request.Op == op).Select(request => JsonDocument.Parse(request.Json).RootElement.GetProperty("chain").GetRawText())];

        public Task<string?> EngineAsync(string request, TimeSpan wait)
        {
            var op = JsonDocument.Parse(request).RootElement.GetProperty("op").GetString()!;
            Requests.Enqueue((op, request));
            if (!_answers.TryGetValue(op, out var queue))
            {
                return Task.FromResult<string?>("""{"ok":true}""");
            }

            lock (queue)
            {
                return Task.FromResult<string?>(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
            }
        }

        public Task<byte[]> ScreenshotAsync() => Task.FromResult(Png);

        public Task ClickAsync(double x, double y)
        {
            Clicks.Add((x, y));
            OnClick?.Invoke();
            return Task.CompletedTask;
        }

        public Task InsertTextAsync(string text)
        {
            Inserted.Add(text);
            return Task.CompletedTask;
        }
    }
}
