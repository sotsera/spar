using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Spar.Bridge;
using Spar.Hosting;

namespace Spar.Tests;

/// <summary>
/// The host's own commands (contract <c>spar</c>) through a document's bridge: the SDK's <c>setColorScheme</c> saves
/// the choice in the data root for the next start, hands it to the window once saved, refuses anything but the three
/// names, and reports <c>notSaved</c> when the data root cannot be written; a session without host commands knows none.
/// </summary>
public sealed class HostCommandsTests : IAsyncLifetime, IDisposable
{
    private const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\"],\"contracts\":\"0123456789abcdef\"}\t";

    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "host-commands", Guid.NewGuid().ToString("N")[..12]);
    private readonly SentMessages _sent = new();
    private readonly List<bool?> _applied = [];
    private TestBackend _backend = null!;

    public ValueTask InitializeAsync()
    {
        _backend = TestBackend.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _backend.DisposeAsync();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SetColorScheme_saves_the_choice_and_hands_it_to_the_window()
    {
        using var session = await OpenAsync(new HostCommands(_root, Apply, NullLogger.Instance));

        session.Receive(Call(1, "\"dark\""));
        var dark = await _sent.NextAsync();
        var savedDark = SavedColorScheme.Read(_root);
        session.Receive(Call(2, "\"system\""));
        var system = await _sent.NextAsync();

        Assert.Equal((BridgeMessageType.Result, true, ""), (dark.Type, dark.Ok!.Value, Text(dark)));
        Assert.Equal((BridgeMessageType.Result, true), (system.Type, system.Ok!.Value));
        Assert.True(savedDark);
        Assert.Null(SavedColorScheme.Read(_root));
        Assert.Equal("system\n", File.ReadAllText(Path.Combine(_root, SavedColorScheme.FileName)));
        Assert.Equal([true, null], _applied);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Theory]
    [InlineData("\"blue\"")]
    [InlineData("\"Dark\"")]
    [InlineData("1")]
    [InlineData("[\"dark\"]")]
    [InlineData("\"dark\" \"light\"")]
    [InlineData("")]
    public async Task Anything_but_the_three_names_is_an_invalid_request_that_changes_nothing(string request)
    {
        using var session = await OpenAsync(new HostCommands(_root, Apply, NullLogger.Instance));

        session.Receive(Call(1, request));
        var failure = await _sent.NextAsync();

        Assert.Equal((BridgeMessageType.Failure, "rejected", "invalid-request"), (failure.Type, failure.Kind, failure.Code));
        Assert.False(File.Exists(Path.Combine(_root, SavedColorScheme.FileName)));
        Assert.Empty(_applied);
    }

    [Fact]
    public async Task A_data_root_that_cannot_be_written_is_notSaved_and_the_window_still_takes_the_choice()
    {
        // A file where the data root's folder should be: the folder cannot be created.
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "");
        using var session = await OpenAsync(new HostCommands(blocked, Apply, NullLogger.Instance));

        session.Receive(Call(1, "\"light\""));
        var error = await _sent.NextAsync();

        Assert.Equal((BridgeMessageType.Result, false, "{\"code\":\"notSaved\"}"), (error.Type, error.Ok!.Value, Text(error)));
        Assert.Equal([false], _applied);
    }

    [Fact]
    public async Task Without_a_window_the_choice_is_only_saved()
    {
        using var session = await OpenAsync(new HostCommands(_root, apply: null, NullLogger.Instance));

        session.Receive(Call(1, "\"light\""));
        var result = await _sent.NextAsync();

        Assert.True(result.Ok);
        Assert.False(SavedColorScheme.Read(_root));
    }

    [Fact]
    public async Task Another_host_command_or_a_session_without_them_is_an_unknown_command()
    {
        using (var session = await OpenAsync(new HostCommands(_root, Apply, NullLogger.Instance)))
        {
            session.Receive(Call(1, "\"dark\"", command: "setTheme"));
            Assert.Equal("unknown-command", (await _sent.NextAsync()).Code);
        }

        using var bare = await OpenAsync(hostCommands: null);
        bare.Receive(Call(1, "\"dark\""));
        Assert.Equal("unknown-command", (await _sent.NextAsync()).Code);
        Assert.False(File.Exists(Path.Combine(_root, SavedColorScheme.FileName)));
    }

    private Task Apply(bool? dark)
    {
        lock (_applied)
        {
            _applied.Add(dark);
        }

        return Task.CompletedTask;
    }

    private async Task<BridgeSession> OpenAsync(HostCommands? hostCommands)
    {
        var session = new BridgeSession(_backend.Dispatcher, TestBackend.Fingerprint, "doc-1", _sent.Add, includeDetails: false)
        {
            HostCommands = hostCommands,
        };
        session.Receive(Hello);
        Assert.Equal(BridgeMessageType.Open, (await _sent.NextAsync()).Type);
        return session;
    }

    private static string Call(long id, string payload, string command = HostCommands.SetColorScheme) =>
        $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"spar\",\"command\":\"{command}\"}}\t{payload}";

    private static string Text(BridgeMessage message) => Encoding.UTF8.GetString(message.Payload.Span);
}
