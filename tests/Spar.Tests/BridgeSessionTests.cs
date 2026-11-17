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

    /// <summary>A desktop host closes a document at navigation; a message of it still in flight must not run a command.</summary>
    [Fact]
    public async Task After_close_a_call_runs_no_command()
    {
        await OpenAsync();

        _session.Close();
        _session.Receive(Call(1, "slow", "wait", ""));
        await _session.WhenIdleAsync();

        Assert.True(await _sent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
        Assert.DoesNotContain("slow.wait", _backend.Started);
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

    [Fact]
    public async Task A_document_with_the_hosts_script_is_pinged_from_its_creation_and_its_pongs_are_reported()
    {
        // The host's injected script answers from the document's creation, before the page says hello.
        using var session = Session(includeDetails: false, answersPing: true);
        var pongs = new List<long>();
        session.Pong += pongs.Add;

        Assert.True(session.Ping(1));
        var ping = await _lastSent.NextAsync();
        session.Receive("{\"type\":\"pong\",\"seq\":1}\t");
        session.Receive("{\"type\":\"pong\",\"seq\":0}\t");

        Assert.Equal((BridgeMessageType.Ping, 1L), (ping.Type, ping.Seq!.Value));
        Assert.Equal([1L], pongs);
        Assert.Single(_dropped);
    }

    [Fact]
    public async Task A_document_without_the_hosts_script_is_never_pinged_even_if_its_hello_lists_ping()
    {
        var opened = 0;
        _session.Opened += () => opened++;
        _session.Receive("{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"ping\"],\"contracts\":\"0123456789abcdef\"}\t");
        Assert.Equal(BridgeMessageType.Open, (await _sent.NextAsync()).Type);

        Assert.False(_session.Ping(1));

        Assert.Equal(1, opened);
        Assert.True(await _sent.StaysQuietAsync(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void A_csp_batch_reaches_the_host_as_its_payload_and_is_no_call()
    {
        var batches = new List<string>();
        _session.Violations += payload => batches.Add(Encoding.UTF8.GetString(payload.Span));

        _session.Receive("{\"type\":\"csp\"}\t{\"groups\":[],\"overflow\":0}");

        Assert.Equal(["{\"groups\":[],\"overflow\":0}"], batches);
        Assert.Empty(_dropped);
    }

    [Fact]
    public async Task A_closed_document_is_not_pinged_and_a_pong_never_touches_a_call()
    {
        using var session = Session(includeDetails: false, answersPing: true);
        session.Receive(Hello);
        Assert.Equal(BridgeMessageType.Open, (await _lastSent.NextAsync()).Type);

        // A pong is no call: its seq is not an id, so the next call may still use id 1.
        session.Receive("{\"type\":\"pong\",\"seq\":5}\t");
        session.Receive(Call(1, "pages", "get", "{\"id\":\"ideas\"}"));
        var result = await _lastSent.NextAsync();
        Assert.Equal((BridgeMessageType.Result, 1L), (result.Type, result.Id!.Value));

        session.Close();
        Assert.False(session.Ping(2));
        Assert.Empty(_dropped);
    }

    private BridgeSession Session(bool includeDetails, bool answersPing = false)
    {
        var sent = _session is null ? _sent : new SentMessages();
        _lastSent = sent;
        var session = new BridgeSession(_backend.Dispatcher, TestBackend.Fingerprint, "doc-1", sent.Add, includeDetails)
        {
            HostAnswersPing = answersPing,
        };
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
