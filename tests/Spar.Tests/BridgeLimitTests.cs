using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spar.Bridge;

namespace Spar.Tests;

/// <summary>
/// The bridge's limits and the redaction of exceptions (decision "Bridge limits, admission and exception redaction:
/// the behaviour"): a size checked before decoding, slots and a bounded queue per document, a result that fails only its
/// own call, and an exception that reaches the page as the call's correlation id.
/// </summary>
public sealed class BridgeLimitTests
{
    private const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\",\"event\",\"closing\"],\"contracts\":\"0123456789abcdef\"}\t";

    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    [Fact]
    public async Task A_call_over_the_size_limit_is_refused_by_its_id_and_the_next_one_runs()
    {
        await using var bridge = await Bridge.OpenAsync(new BridgeOptions { MaxMessageBytes = 1024 });

        bridge.Session.Receive(Call(1, "echo", "back", Quoted(2000)));
        bridge.Session.Receive(Call(2, "echo", "back", "\"small\""));

        Assert.Equal(("rejected", "too-large", "notStarted"), Failure(await bridge.Sent.NextAsync()));
        Assert.Equal(BridgeMessageType.Result, (await bridge.Sent.NextAsync()).Type);
        Assert.Contains(bridge.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("call 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_oversized_message_with_no_call_header_gets_no_reply()
    {
        await using var bridge = await Bridge.OpenAsync(new BridgeOptions { MaxMessageBytes = 1024 });

        bridge.Session.Receive(new string('x', 4000));

        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Contains(bridge.Dropped, reason => reason.Contains("over the limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Calls_past_the_slots_wait_and_start_in_order_and_past_the_queue_are_busy()
    {
        await using var bridge = await Bridge.OpenAsync(new BridgeOptions { CallsInFlight = 2, QueuedCalls = 1 });

        bridge.Session.Receive(Call(1, "slow", "wait", ""));
        bridge.Session.Receive(Call(2, "slow", "wait", ""));
        await WaitUntilAsync(() => bridge.Backend.Started.Count == 2);
        bridge.Session.Receive(Call(3, "slow", "wait", ""));
        bridge.Session.Receive(Call(4, "slow", "wait", ""));

        var busy = await bridge.Sent.NextAsync();
        Assert.Equal((4L, "rejected", "busy", "notStarted"), (busy.Id, busy.Kind, busy.Code, busy.Execution));
        Assert.Equal(2, bridge.Backend.Started.Count);

        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":1}\t");
        Assert.Equal((1L, "cancelled"), Kind(await bridge.Sent.NextAsync()));
        await WaitUntilAsync(() => bridge.Backend.Started.Count == 3);

        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":2}\t");
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":3}\t");
        var ended = new[] { await bridge.Sent.NextAsync(), await bridge.Sent.NextAsync() }.Select(Kind).Order().ToList();
        Assert.Equal([(2L, "cancelled"), (3L, "cancelled")], ended);
    }

    [Fact]
    public async Task A_waiting_call_that_is_cancelled_never_starts()
    {
        await using var bridge = await Bridge.OpenAsync(new BridgeOptions { CallsInFlight = 1 });
        bridge.Session.Receive(Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => bridge.Backend.Started.Count == 1);

        bridge.Session.Receive(Call(2, "echo", "back", "\"never\""));
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":2}\t");
        var cancelled = await bridge.Sent.NextAsync();
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":1}\t");
        await bridge.Sent.NextAsync();

        Assert.Equal((2L, "cancelled", "cancelled-before-start", "notStarted"), (cancelled.Id, cancelled.Kind, cancelled.Code, cancelled.Execution));
        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task The_queue_is_also_bounded_by_the_bytes_it_holds()
    {
        await using var bridge = await Bridge.OpenAsync(new BridgeOptions { CallsInFlight = 1, QueuedCalls = 10, QueuedBytes = 100 });
        bridge.Session.Receive(Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => bridge.Backend.Started.Count == 1);

        bridge.Session.Receive(Call(2, "echo", "back", Quoted(80)));
        bridge.Session.Receive(Call(3, "echo", "back", Quoted(30)));

        var busy = await bridge.Sent.NextAsync();
        Assert.Equal((3L, "busy"), (busy.Id, busy.Code));
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":1}\t");
        var replies = new[] { await bridge.Sent.NextAsync(), await bridge.Sent.NextAsync() };
        Assert.Contains(replies, r => r.Id == 2 && r.Type == BridgeMessageType.Result);
    }

    [Fact]
    public async Task A_result_over_the_limit_fails_only_its_own_call()
    {
        await using var bridge = await Bridge.OpenAsync(new BridgeOptions { MaxResultBytes = 100 });

        bridge.Session.Receive(Call(1, "echo", "back", Quoted(200)));
        var failed = await bridge.Sent.NextAsync();
        bridge.Session.Receive(Call(2, "echo", "back", "\"small\""));

        Assert.Equal(("response", "too-large", "completed"), Failure(failed));
        Assert.Equal(BridgeMessageType.Result, (await bridge.Sent.NextAsync()).Type);
        Assert.Contains(bridge.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("echo.back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_exception_reaches_the_page_as_the_calls_trace_id_and_its_details_go_to_the_log()
    {
        await using var bridge = await Bridge.OpenAsync();

        bridge.Session.Receive(Call(1, "boom", "throw", "", TraceParent));
        var failed = await bridge.Sent.NextAsync();

        Assert.Equal(("failed", "failed", "unknown"), Failure(failed));
        Assert.Equal("{\"correlation\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}", Encoding.UTF8.GetString(failed.Payload.Span));
        var entry = Assert.Single(bridge.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("4bf92f3577b34da6a3ce929d0e0e4736", entry.Message, StringComparison.Ordinal);
        Assert.Contains("boom.throw", entry.Message, StringComparison.Ordinal);
        Assert.IsType<IOException>(entry.Exception);
    }

    [Fact]
    public async Task Development_adds_the_detail_beside_the_correlation_id()
    {
        await using var bridge = await Bridge.OpenAsync(includeDetails: true);

        bridge.Session.Receive(Call(1, "boom", "throw", "", TraceParent));
        var payload = JsonDocument.Parse((await bridge.Sent.NextAsync()).Payload).RootElement;

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", payload.GetProperty("correlation").GetString());
        Assert.Equal("IOException: disk full", payload.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_call_without_a_trace_gets_a_new_id_that_the_log_names_too()
    {
        await using var bridge = await Bridge.OpenAsync();

        bridge.Session.Receive(Call(1, "boom", "throw", ""));
        var correlation = JsonDocument.Parse((await bridge.Sent.NextAsync()).Payload).RootElement.GetProperty("correlation").GetString()!;

        Assert.Matches("^[0-9a-f]{32}$", correlation);
        Assert.Contains(correlation, Assert.Single(bridge.Log.Entries, e => e.Level == LogLevel.Error).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_carries_no_correlation_and_logs_no_error()
    {
        await using var bridge = await Bridge.OpenAsync();

        bridge.Session.Receive(Call(1, "pages", "missing", ""));

        Assert.True((await bridge.Sent.NextAsync()).Payload.IsEmpty);
        Assert.DoesNotContain(bridge.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(nameof(BridgeOptions.MaxMessageBytes))]
    [InlineData(nameof(BridgeOptions.MaxResultBytes))]
    [InlineData(nameof(BridgeOptions.CallsInFlight))]
    [InlineData(nameof(BridgeOptions.QueuedCalls))]
    [InlineData(nameof(BridgeOptions.QueuedBytes))]
    public void A_limit_below_one_is_refused_when_the_app_sets_it(string name)
    {
        var builder = new SparBuilder([]);

        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Bridge(o => typeof(BridgeOptions).GetProperty(name)!.SetValue(o, 0)));

        Assert.Equal(name, refused.ParamName);
    }

    private static string Call(long id, string contract, string command, string payload, string? traceParent = null)
    {
        var trace = traceParent is null ? "" : $",\"traceparent\":\"{traceParent}\"";
        return $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"{command}\"{trace}}}\t{payload}";
    }

    /// <summary>A JSON string of <paramref name="bytes"/> bytes, quotes included.</summary>
    private static string Quoted(int bytes) => "\"" + new string('a', bytes - 2) + "\"";

    private static (string?, string?, string?) Failure(BridgeMessage message)
    {
        Assert.Equal(BridgeMessageType.Failure, message.Type);
        return (message.Kind, message.Code, message.Execution);
    }

    private static (long, string?) Kind(BridgeMessage message) => (message.Id!.Value, message.Kind);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never became true.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>An opened session over a backend with the given limits, its sent messages and its log.</summary>
    private sealed class Bridge : IAsyncDisposable
    {
        private Bridge(TestBackend backend, CapturedLog log, bool includeDetails)
        {
            Backend = backend;
            Log = log;
            Session = new BridgeSession(backend.Dispatcher, TestBackend.Fingerprint, "doc-1", Sent.Add, includeDetails);
            Session.Dropped += reason => { lock (Dropped) { Dropped.Add(reason); } };
        }

        public TestBackend Backend { get; }

        public CapturedLog Log { get; }

        public BridgeSession Session { get; }

        public SentMessages Sent { get; } = new();

        public List<string> Dropped { get; } = [];

        public static async Task<Bridge> OpenAsync(BridgeOptions? limits = null, bool includeDetails = false)
        {
            var log = new CapturedLog();
            var bridge = new Bridge(TestBackend.Create(limits, log), log, includeDetails);
            bridge.Session.Receive(Hello);
            Assert.Equal(BridgeMessageType.Open, (await bridge.Sent.NextAsync()).Type);
            return bridge;
        }

        public async ValueTask DisposeAsync()
        {
            Session.Dispose();
            await Backend.DisposeAsync();
        }
    }
}
