using System.Text;
using Spar.Bridge;

namespace Spar.Tests;

/// <summary>One document's bridge: hello and open, ids, cancels, one terminal per call, and nothing after close.</summary>
public sealed class BridgeSessionTests : IAsyncLifetime
{
    private const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\",\"event\",\"closing\"],\"contracts\":\"0123456789abcdef\"}\t";

    private readonly SentMessages _sent = new();
    private readonly List<string> _dropped = [];
    private TestBackend _backend = null!;
    private BridgeSession _session = null!;
    private SentMessages _lastSent = new();

    public ValueTask InitializeAsync()
    {
        _backend = TestBackend.Create();
        _session = Session(includeDetails: false);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _session.Dispose();
        await _backend.DisposeAsync();
    }

    [Fact]
    public async Task Hello_is_answered_with_open()
    {
        _session.Receive("{\"type\":\"hello\",\"protocol\":3,\"types\":[\"open\"],\"contracts\":\"0123456789abcdef\"}\t");

        var open = await _sent.NextAsync();

        Assert.Equal((BridgeMessageType.Open, 1, "doc-1", TestBackend.Fingerprint), (open.Type, open.Protocol, open.Doc, open.Contracts));
    }

    [Fact]
    public async Task A_call_returns_its_value_or_its_declared_error()
    {
        await OpenAsync();

        _session.Receive(Call(1, "pages", "get", "{\"id\":\"ideas\"}"));
        var value = await _sent.NextAsync();
        _session.Receive(Call(2, "pages", "get", "{\"id\":\"missing\"}"));
        var error = await _sent.NextAsync();

        Assert.Equal((BridgeMessageType.Result, 1L, true, "{\"id\":\"ideas\",\"title\":\"Ideas\"}"), (value.Type, value.Id!.Value, value.Ok!.Value, Text(value)));
        Assert.Equal((BridgeMessageType.Result, 2L, false, "{\"code\":\"notFound\",\"id\":\"missing\"}"), (error.Type, error.Id!.Value, error.Ok!.Value, Text(error)));
    }

    [Fact]
    public async Task A_call_before_hello_is_rejected()
    {
        _session.Receive(Call(1, "pages", "get", "{}"));

        Assert.Equal(("rejected", "invalid-request", "notStarted"), Failure(await _sent.NextAsync()));
    }

    [Fact]
    public async Task A_page_built_from_other_contracts_has_every_call_rejected()
    {
        _session.Receive("{\"type\":\"hello\",\"protocol\":1,\"types\":[],\"contracts\":\"ffffffffffffffff\"}\t");
        var open = await _sent.NextAsync();

        _session.Receive(Call(1, "pages", "get", "{}"));

        Assert.Equal(TestBackend.Fingerprint, open.Contracts);
        Assert.Equal(("rejected", "contract-mismatch", "notStarted"), Failure(await _sent.NextAsync()));
    }

    [Fact]
    public async Task A_cancel_of_a_running_call_is_confirmed()
    {
        await OpenAsync();
        _session.Receive(Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => _backend.Started.Contains("slow.wait"));

        _session.Receive("{\"type\":\"cancel\",\"id\":1}\t");

        var reply = await _sent.NextAsync();
        Assert.Equal((1L, ("cancelled", "cancelled", "stopped")), (reply.Id!.Value, Failure(reply)));
    }

    [Fact]
    public async Task A_cancel_that_overtakes_its_call_stops_it_before_it_starts()
    {
        await OpenAsync();

        _session.Receive("{\"type\":\"cancel\",\"id\":5}\t");
        _session.Receive(Call(5, "slow", "wait", ""));

        Assert.Equal(("cancelled", "cancelled-before-start", "notStarted"), Failure(await _sent.NextAsync()));
        Assert.Empty(_backend.Started);
    }

    [Fact]
    public async Task A_repeated_or_lower_id_gets_no_reply()
    {
        await OpenAsync();
        _session.Receive(Call(4, "pages", "get", "{}"));
        await _sent.NextAsync();

        _session.Receive(Call(4, "pages", "get", "{}"));
        _session.Receive(Call(3, "pages", "get", "{}"));

        Assert.True(await _sent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(2, _dropped.Count);
    }

    [Fact]
    public async Task A_malformed_call_with_a_new_id_is_rejected()
    {
        await OpenAsync();

        _session.Receive("{\"type\":\"call\",\"id\":3,\"contract\":\"pages\"}\t{}");

        var reply = await _sent.NextAsync();
        Assert.Equal((3L, ("rejected", "invalid-request", "notStarted")), (reply.Id!.Value, Failure(reply)));
    }

    [Fact]
    public async Task An_unknown_message_for_a_pending_call_fails_that_call_and_its_late_result_is_dropped()
    {
        await OpenAsync();
        _session.Receive(Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => _backend.Started.Contains("slow.wait"));

        _session.Receive("{\"type\":\"pause\",\"id\":1}\t");

        Assert.Equal(("protocol", "malformed", "unknown"), Failure(await _sent.NextAsync()));
        await _session.WhenIdleAsync();
        Assert.True(await _sent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task After_close_nothing_more_is_sent()
    {
        await OpenAsync();
        _session.Receive(Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => _backend.Started.Contains("slow.wait"));

        _session.Close();
        await _session.WhenIdleAsync();
        _session.Receive(Call(2, "pages", "get", "{}"));

        Assert.True(await _sent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Details_reach_the_page_only_when_the_host_allows_them()
    {
        using var development = Session(includeDetails: true);
        var developmentSent = _lastSent;
        await OpenAsync();
        development.Receive(Hello);
        await developmentSent.NextAsync();

        _session.Receive(Call(1, "boom", "throw", ""));
        development.Receive(Call(1, "boom", "throw", ""));

        // Both carry the correlation id (BridgeLimitTests covers it); only Development adds the detail.
        var production = await _sent.NextAsync();
        var withDetail = await developmentSent.NextAsync();
        Assert.Equal(("failed", "failed", "unknown"), (production.Kind, production.Code, production.Execution));
        Assert.DoesNotContain("detail", Text(production), StringComparison.Ordinal);
        Assert.Contains("\"detail\":\"IOException: disk full\"", Text(withDetail), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_events_go_only_to_pages_that_listed_them()
    {
        using var quiet = Session(includeDetails: false);
        var quietSent = _lastSent;
        quiet.Receive("{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\"],\"contracts\":\"0123456789abcdef\"}\t");
        await quietSent.NextAsync();
        await OpenAsync();

        _session.SendEvent("backend-updated", Encoding.UTF8.GetBytes("{\"revision\":3}"));
        quiet.SendEvent("backend-updated", Encoding.UTF8.GetBytes("{\"revision\":3}"));

        var sent = await _sent.NextAsync();
        Assert.Equal((BridgeMessageType.Event, "backend-updated", "{\"revision\":3}"), (sent.Type, sent.Name, Text(sent)));
        Assert.True(await quietSent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Ready_and_close_work_reach_the_host()
    {
        var ready = false;
        var closingDone = false;
        _session.Ready += () => ready = true;
        _session.ClosingDone += () => closingDone = true;
        await OpenAsync();

        _session.RequestClosing();
        var closing = await _sent.NextAsync();
        _session.Receive("{\"type\":\"ready\"}\t");
        _session.Receive("{\"type\":\"closing-done\"}\t");

        Assert.Equal(BridgeMessageType.Closing, closing.Type);
        Assert.True(ready);
        Assert.True(closingDone);
    }

    private BridgeSession Session(bool includeDetails)
    {
        var sent = _session is null ? _sent : new SentMessages();
        _lastSent = sent;
        var session = new BridgeSession(_backend.Dispatcher, TestBackend.Fingerprint, "doc-1", sent.Add, includeDetails);
        session.Dropped += reason => { lock (_dropped) { _dropped.Add(reason); } };
        return session;
    }

    private async Task OpenAsync()
    {
        _session.Receive(Hello);
        Assert.Equal(BridgeMessageType.Open, (await _sent.NextAsync()).Type);
    }

    private static string Call(long id, string contract, string command, string payload) =>
        $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"{command}\"}}\t{payload}";

    private static (string?, string?, string?) Failure(BridgeMessage message)
    {
        Assert.Equal(BridgeMessageType.Failure, message.Type);
        return (message.Kind, message.Code, message.Execution);
    }

    private static string Text(BridgeMessage message) => Encoding.UTF8.GetString(message.Payload.Span);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never became true.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
