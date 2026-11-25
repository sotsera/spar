using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spar.Devtools;

namespace Spar.Tests;

/// <summary>
/// The devtools' endpoint (decision "M2a's decide-first questions"): the discovery file, the owner-only socket, the
/// JSON-RPC rules (initialize and its token first, duplicate ids, stale targets) and the backend tools.
/// </summary>
public sealed class DevtoolsServerTests : IAsyncLifetime
{
    private const string Run = "c0ffee01";

    private readonly DirectoryInfo _obj = Directory.CreateTempSubdirectory("spar-devtools-obj-");
    private readonly DevtoolsLog _log = new(TimeProvider.System);
    private TestBackend _backend = null!;
    private DevtoolsServer _server = null!;

    private string File => Path.Combine(_obj.FullName, "spar-devtools.json");

    public ValueTask InitializeAsync()
    {
        _backend = TestBackend.Create();
        var pages = new DevtoolsPages();
        _server = DevtoolsServer.Start(new DevtoolsContext(_backend.App, Run, "windows", File, pages, _log), _log.CreateLogger("Spar.Devtools"))
            ?? throw new InvalidOperationException("The devtools did not listen.");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _backend.DisposeAsync();
        _obj.Delete(recursive: true);
    }

    [Fact]
    public async Task The_discovery_file_is_owner_only_names_the_run_and_goes_at_the_end()
    {
        var recorded = DevtoolsFile.Read(File);

        Assert.NotNull(recorded);
        Assert.Equal((Environment.ProcessId, Run, "windows"), (recorded.ProcessId, recorded.Run, recorded.Host));
        Assert.Equal(OperatingSystem.IsWindows() ? "pipe" : "unix", recorded.Transport);
        Assert.Matches("^[0-9a-f]{64}$", recorded.Token);
        if (OperatingSystem.IsWindows())
        {
            AssertOwnerOnly(File);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, System.IO.File.GetUnixFileMode(File));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, System.IO.File.GetUnixFileMode(recorded.Address));
        }

        await _server.DisposeAsync();
        Assert.False(System.IO.File.Exists(File));
    }

    [Fact]
    public async Task A_later_runs_file_is_left_when_an_earlier_run_ends()
    {
        var later = DevtoolsFile.Read(File)! with { Run = "0ther0ne" };
        Assert.True(later.Write(File));

        await _server.DisposeAsync();

        Assert.Equal("0ther0ne", DevtoolsFile.Read(File)?.Run);
    }

    [Fact]
    public async Task The_first_request_is_initialize_with_the_token_and_a_wrong_one_ends_the_connection()
    {
        await using (var client = await Client.OpenAsync(File))
        {
            var refused = await client.RequestAsync("version");
            Assert.Equal("unauthorized", Reason(refused));
            Assert.True(await client.EndedAsync());
        }

        await using (var client = await Client.OpenAsync(File))
        {
            var refused = await client.RequestAsync("initialize", """{"token":"0000"}""");
            Assert.Equal("unauthorized", Reason(refused));
            Assert.True(await client.EndedAsync());
        }

        await using var good = await Client.OpenAsync(File);
        var hello = await good.InitializeAsync();
        Assert.Equal(1, hello.GetProperty("protocol").GetInt32());
        Assert.Equal(Run, hello.GetProperty("run").GetString());
        Assert.Contains("screenshot", hello.GetProperty("tools").EnumerateArray().Select(tool => tool.GetString()));
    }

    [Fact]
    public async Task A_reused_id_another_runs_target_a_bad_line_and_an_unknown_tool_are_refused()
    {
        await using var client = await Client.OpenAsync(File);
        await client.InitializeAsync();

        Assert.Equal("success", Result(await client.RequestAsync("call", """{"command":"call.caller"}""", id: 7)).GetProperty("outcome").GetString());
        var again = await client.RequestAsync("call", """{"command":"call.caller"}""", id: 7);
        var stale = await client.RequestAsync("version", $$"""{"run":"deadbeef"}""");
        var unknown = await client.RequestAsync("teleport");
        var garbled = await client.SendRawAsync("{not json");

        Assert.Equal("duplicate_id", Reason(again));
        Assert.Single(_backend.App.Dispatcher.Recorder!.Since(0), call => call.Command == "caller");
        Assert.Equal("stale_target", Reason(stale));
        Assert.Equal(JsonRpc.MethodNotFound, Code(unknown));
        Assert.Equal(JsonRpc.ParseError, Code(garbled));
        Assert.Equal(Run, Result(await client.RequestAsync("version", $$"""{"run":"{{Run}}"}""")).GetProperty("run").GetString());
    }

    [Fact]
    public async Task Call_runs_the_command_as_the_devtools_and_calls_lists_it()
    {
        await using var client = await Client.OpenAsync(File);
        await client.InitializeAsync();

        var caller = Result(await client.RequestAsync("call", """{"command":"call.caller"}"""));
        var declared = Result(await client.RequestAsync("call", """{"command":"pages.get","request":{"id":"missing"}}"""));
        var failed = Result(await client.RequestAsync("call", """{"command":"boom.throw"}"""));
        var missing = await client.RequestAsync("call", """{"command":"pages.teleport"}""");
        var calls = Result(await client.RequestAsync("calls")).GetProperty("calls").EnumerateArray().ToList();

        Assert.Equal("Devtools", caller.GetProperty("value").GetString());
        Assert.Equal(("error", "notFound"), (declared.GetProperty("outcome").GetString(), declared.GetProperty("error").GetProperty("code").GetString()));
        Assert.Equal(("failure", "failed"), (failed.GetProperty("outcome").GetString(), failed.GetProperty("failure").GetProperty("kind").GetString()));
        Assert.Equal("not_found", Reason(missing));
        Assert.Equal(["call.caller", "pages.get", "boom.throw"], calls.Select(call => call.GetProperty("command").GetString()));
        Assert.All(calls, call => Assert.Equal("devtools", call.GetProperty("caller").GetString()));
        Assert.Equal(["success", "error", "failed"], calls.Select(call => call.GetProperty("outcome").GetString()));
        Assert.Equal("notFound", calls[1].GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_call_past_its_wait_is_unfinished_and_keeps_running()
    {
        await using var client = await Client.OpenAsync(File);
        await client.InitializeAsync();

        var waited = await client.RequestAsync("call", """{"command":"slow.wait","timeout":200}""");
        var calls = Result(await client.RequestAsync("calls")).GetProperty("calls");

        Assert.Equal("timeout", Reason(waited));
        Assert.Contains("may still be running", waited.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("running", Assert.Single(calls.EnumerateArray()).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Logs_merge_the_backend_the_page_and_violations_and_filter_them()
    {
        var backend = _log.CreateLogger("Notebook.Pages");
        backend.LogInformation("Saved {Page}", "ideas");
        backend.LogDebug("cache warm");
        _log.PageConsole("warn", "slow image");
        _log.PageError("TypeError: x is undefined");
        _log.CreateLogger("Spar.Host").Log(LogLevel.Warning, new EventId(1, "CspViolation"), "blocked", null, (state, _) => state);
        await using var client = await Client.OpenAsync(File);
        await client.InitializeAsync();

        var all = Entries(await client.RequestAsync("logs", """{"level":"debug"}"""));
        var standard = Entries(await client.RequestAsync("logs"));
        var page = Entries(await client.RequestAsync("logs", """{"source":"page"}"""));
        var recent = Entries(await client.RequestAsync("logs", """{"since":"1h","level":"warn"}"""));
        var wrong = await client.RequestAsync("logs", """{"since":"yesterday"}""");

        Assert.Contains(all, entry => entry is ("backend", "debug", "cache warm"));
        Assert.DoesNotContain(standard, entry => entry.Message == "cache warm");
        Assert.Equal([("page", "warn", "slow image"), ("page", "error", "TypeError: x is undefined")], page);
        Assert.Contains(("csp", "warn", "blocked"), recent);
        Assert.DoesNotContain(recent, entry => entry.Level == "info");
        Assert.Equal("invalid", Reason(wrong));
    }

    [Fact]
    public async Task Version_and_contracts_describe_the_run()
    {
        await using var client = await Client.OpenAsync(File);
        await client.InitializeAsync();

        var version = Result(await client.RequestAsync("version"));
        var contracts = Result(await client.RequestAsync("contracts"));

        Assert.Equal((Run, "windows", Environment.ProcessId), (version.GetProperty("run").GetString(), version.GetProperty("host").GetString(), version.GetProperty("pid").GetInt32()));
        Assert.False(version.GetProperty("page").GetBoolean());
        Assert.Equal(TestBackend.Fingerprint, contracts.GetProperty("fingerprint").GetString());
        var names = contracts.GetProperty("commands").EnumerateArray().Select(command => command.GetProperty("name").GetString()).ToList();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Contains(contracts.GetProperty("commands").EnumerateArray(), command => command.GetProperty("name").GetString() == "slow.limited" && command.GetProperty("timeoutSeconds").GetInt32() == 1);
        Assert.False(contracts.TryGetProperty("snapshot", out _));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    public void Since_reads_a_duration_back_from_now(string text, int seconds)
    {
        var since = DevtoolsTools.Since(text);

        Assert.InRange((DateTimeOffset.UtcNow - since).TotalSeconds, seconds - 2, seconds + 2);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero), DevtoolsTools.Since("2026-11-25T21:00:00Z"));
    }

    [Fact]
    public async Task A_line_longer_than_the_limit_ends_the_read_and_a_crlf_line_reads_as_its_text()
    {
        var pending = new JsonRpc.Pending();
        var buffer = new byte[64];
        var line = new System.Buffers.ArrayBufferWriter<byte>();
        using var crlf = new MemoryStream("{\"a\":1}\r\n{\"b\":2}\n"u8.ToArray());
        using var huge = new MemoryStream(new byte[JsonRpc.MaxRequest + 10]);

        var first = await JsonRpc.ReadLineAsync(crlf, line, buffer, pending, CancellationToken.None);
        var second = await JsonRpc.ReadLineAsync(crlf, line, buffer, pending, CancellationToken.None);
        var end = await JsonRpc.ReadLineAsync(crlf, line, buffer, pending, CancellationToken.None);

        Assert.Equal(("{\"a\":1}", "{\"b\":2}", null), (Encoding.UTF8.GetString(first!), Encoding.UTF8.GetString(second!), end));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await JsonRpc.ReadLineAsync(huge, line, new byte[4096], new JsonRpc.Pending(), CancellationToken.None));
    }

    private static List<(string Source, string Level, string Message)> Entries(JsonElement response) =>
        [.. Result(response).GetProperty("entries").EnumerateArray()
            .Select(entry => (entry.GetProperty("source").GetString()!, entry.GetProperty("level").GetString()!, entry.GetProperty("message").GetString()!))];

    private static JsonElement Result(JsonElement response) =>
        response.TryGetProperty("result", out var result) ? result : throw new InvalidOperationException("No result: " + response);

    private static string? Reason(JsonElement response) =>
        response.TryGetProperty("error", out var error) && error.TryGetProperty("data", out var data) && data.TryGetProperty("reason", out var reason)
            ? reason.GetString()
            : null;

    private static int Code(JsonElement response) => response.GetProperty("error").GetProperty("code").GetInt32();

    private static void AssertOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var security = new FileInfo(path).GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(security.AreAccessRulesProtected);
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
        Assert.Equal(identity.User, rule.IdentityReference);
    }

    /// <summary>A line-at-a-time JSON-RPC client over the run's socket, as the spar tool connects.</summary>
    internal sealed class Client : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly StreamReader _reader;
        private readonly string _token;
        private int _id;

        private Client(Stream stream, string token)
        {
            _stream = stream;
            _reader = new StreamReader(stream, new UTF8Encoding(false));
            _token = token;
        }

        public static async Task<Client> OpenAsync(string file)
        {
            var recorded = DevtoolsFile.Read(file) ?? throw new InvalidOperationException("No discovery file.");
            Stream stream;
            if (recorded.Transport == "pipe")
            {
                var pipe = new NamedPipeClientStream(".", recorded.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000);
                stream = pipe;
            }
            else
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(recorded.Address));
                stream = new NetworkStream(socket, ownsSocket: true);
            }

            return new Client(stream, recorded.Token);
        }

        public async Task<JsonElement> InitializeAsync() => Result(await RequestAsync("initialize", $$"""{"token":"{{_token}}"}"""));

        public Task<JsonElement> RequestAsync(string method, string parameters = "{}", int? id = null) =>
            SendRawAsync($$"""{"jsonrpc":"2.0","id":{{id ?? ++_id + 1000}},"method":"{{method}}","params":{{parameters}}}""");

        public async Task<JsonElement> SendRawAsync(string line)
        {
            await _stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
            await _stream.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var answer = await _reader.ReadLineAsync(timeout.Token) ?? throw new IOException("The connection ended.");
            using var document = JsonDocument.Parse(answer);
            return document.RootElement.Clone();
        }

        public async Task<bool> EndedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                return await _reader.ReadLineAsync(timeout.Token) is null;
            }
            catch (IOException)
            {
                return true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            await _stream.DisposeAsync();
        }
    }
}
