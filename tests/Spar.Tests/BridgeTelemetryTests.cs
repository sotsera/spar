using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Spar.Bridge;

namespace Spar.Tests;

/// <summary>Activity and meter listeners are process-wide, so these tests run alone, after the parallel ones.</summary>
[CollectionDefinition(nameof(TelemetryCollection), DisableParallelization = true)]
public sealed class TelemetryCollection;

/// <summary>
/// The always-on instrumentation of bridge commands (plan section 6b): a server activity per admitted call, a child of the
/// page's <c>traceparent</c>, one <c>spar.call.duration</c> sample per call from receipt, and refusals within a budget.
/// </summary>
[Collection(nameof(TelemetryCollection))]
public sealed class BridgeTelemetryTests : IDisposable
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

    private const string TraceParent = $"00-{TraceId}-b7ad6b7169203331-01";

    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<(double Seconds, Dictionary<string, object?> Tags)> _durations = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public BridgeTelemetryTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Spar",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Spar" && instrument.Name == "spar.call.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            _durations.Enqueue((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public async Task A_command_runs_in_a_server_activity_that_continues_the_pages_trace()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(TestBridge.Call(1, "echo", "back", "\"hi\"", TraceParent));
        await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        var activity = Assert.Single(_activities);
        Assert.Equal(("echo/back", ActivityKind.Server), (activity.DisplayName, activity.Kind));
        Assert.Equal((TraceId, "b7ad6b7169203331"), (activity.TraceId.ToHexString(), activity.ParentSpanId.ToHexString()));
        Assert.Equal(
            [("rpc.method", "back"), ("rpc.service", "echo"), ("rpc.system", "spar"), ("spar.outcome", "success")],
            activity.Tags.Where(t => t.Key.StartsWith("rpc.", StringComparison.Ordinal) || t.Key == "spar.outcome").Select(t => (t.Key, t.Value)).Order());
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    [Fact]
    public async Task A_stream_has_one_activity_with_its_item_count_and_a_subscription_its_drops()
    {
        await using var bridge = await TestBridge.OpenAsync();

        // The page's trace on both, so only this test's spans are read: activity listeners are process-wide.
        bridge.Session.Receive(Traced(Streams.StreamSessionTests.Stream(1, "rows", "count", credits: 256, "100")));
        while ((await bridge.Sent.NextAsync()).Type != Spar.Bridge.BridgeMessageType.End)
        {
        }

        bridge.Session.Receive(Traced(Streams.StreamSessionTests.Stream(2, "feed", "ticks", credits: 1, "10")));
        await bridge.Sent.NextAsync();
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":2}\t");
        await bridge.Session.WhenIdleAsync();

        var activities = _activities.Where(a => a.TraceId.ToHexString() == TraceId).ToList();
        Assert.Equal(2, activities.Count);
        var stream = activities.Single(a => a.DisplayName == "rows/count");
        var subscription = activities.Single(a => a.DisplayName == "feed/ticks");
        Assert.Equal(("stream", 100L), (stream.GetTagItem("spar.stream.kind"), stream.GetTagItem("spar.stream.items")));
        Assert.Equal("subscription", subscription.GetTagItem("spar.stream.kind"));
        Assert.Equal(5L, subscription.GetTagItem("spar.stream.dropped"));
    }

    [Fact]
    public async Task A_command_that_throws_marks_its_activity_and_the_page_gets_its_trace_id()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(TestBridge.Call(1, "boom", "throw", ""));
        var failure = await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        var activity = Assert.Single(_activities);
        var correlation = JsonDocument.Parse(failure.Payload).RootElement.GetProperty("correlation").GetString();
        Assert.Equal(activity.TraceId.ToHexString(), correlation);
        Assert.Equal((ActivityStatusCode.Error, "failed"), (activity.Status, activity.GetTagItem("error.type")));
        Assert.Contains(activity.Events, e => e.Name == "exception" && (string?)e.Tags.First(t => t.Key == "exception.type").Value == typeof(IOException).FullName);
    }

    [Fact]
    public async Task A_command_without_a_trace_starts_a_new_one_and_never_inherits_the_receiving_threads()
    {
        await using var bridge = await TestBridge.OpenAsync();
        using var outer = new Activity("host request").Start();

        bridge.Session.Receive(TestBridge.Call(1, "echo", "back", "\"hi\""));
        await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        var activity = Assert.Single(_activities);
        Assert.Equal(default, activity.ParentSpanId);
        Assert.NotEqual(outer.TraceId, activity.TraceId);
    }

    [Fact]
    public async Task Each_command_records_its_duration_with_its_outcome()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(TestBridge.Call(1, "echo", "back", "\"hi\""));
        await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(2, "pages", "get", "{\"id\":\"missing\"}"));
        await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(3, "boom", "throw", ""));
        await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        var recorded = _durations.Select(d => ((string?)d.Tags["rpc.service"], (string?)d.Tags["spar.outcome"])).Order().ToList();
        Assert.Equal([("boom", "failed"), ("echo", "success"), ("pages", "error")], recorded);
        Assert.All(_durations, d => Assert.InRange(d.Seconds, 0, 10));
    }

    [Fact]
    public async Task Time_in_the_queue_counts_in_the_span_and_the_duration()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { CallsInFlight = 1 });
        bridge.Session.Receive(TestBridge.Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => bridge.Backend.Started.Count == 1);

        bridge.Session.Receive(TestBridge.Call(2, "echo", "back", "\"hi\""));
        await Task.Delay(150, TestContext.Current.CancellationToken);
        bridge.Session.Receive("{\"type\":\"cancel\",\"id\":1}\t");
        await bridge.Sent.NextAsync();
        await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        var echo = Assert.Single(_activities, a => a.DisplayName == "echo/back");
        var started = Assert.Single(echo.Events, e => e.Name == "started");
        Assert.InRange((double)echo.GetTagItem("spar.queue.seconds")!, 0.1, 10);
        Assert.InRange(started.Timestamp - echo.StartTimeUtc, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        Assert.InRange(Assert.Single(_durations, d => (string?)d.Tags["rpc.service"] == "echo").Seconds, 0.1, 10);
    }

    [Fact]
    public async Task A_call_refused_at_the_door_is_counted_and_traced_with_its_code()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { CallsInFlight = 1, QueuedCalls = 1 });
        bridge.Session.Receive(TestBridge.Call(1, "slow", "wait", ""));
        await WaitUntilAsync(() => bridge.Backend.Started.Count == 1);
        bridge.Session.Receive(TestBridge.Call(2, "echo", "back", "\"a\""));

        bridge.Session.Receive(TestBridge.Call(3, "echo", "back", "\"b\""));
        Assert.Equal("busy", (await bridge.Sent.NextAsync()).Code);

        var refused = Assert.Single(_durations);
        Assert.Equal(("echo", "rejected", "busy"), ((string?)refused.Tags["rpc.service"], (string?)refused.Tags["spar.outcome"], (string?)refused.Tags["error.type"]));
        var span = Assert.Single(_activities);
        Assert.Equal(("echo/back", ActivityStatusCode.Error, "busy"), (span.DisplayName, span.Status, span.GetTagItem("error.type")));
        bridge.Session.Close();
    }

    [Fact]
    public async Task Names_the_app_does_not_have_are_recorded_as_unknown()
    {
        await using var bridge = await TestBridge.OpenAsync();

        bridge.Session.Receive(TestBridge.Call(1, "made", "up", ""));
        Assert.Equal("unknown-command", (await bridge.Sent.NextAsync()).Code);
        await bridge.Session.WhenIdleAsync();

        Assert.Equal("unknown/unknown", Assert.Single(_activities).DisplayName);
        var tags = Assert.Single(_durations).Tags;
        Assert.Equal(("unknown", "unknown"), ((string?)tags["rpc.service"], (string?)tags["rpc.method"]));
    }

    [Fact]
    public async Task A_flood_of_refusals_is_counted_in_full_but_traced_and_logged_within_the_budget()
    {
        var time = new ManualTime();
        var mismatch = TestBridge.Hello.Replace("0123456789abcdef", "ffffffffffffffff", StringComparison.Ordinal);
        await using var bridge = await TestBridge.OpenAsync(time: time, hello: mismatch);

        for (var id = 1; id <= 30; id++)
        {
            bridge.Session.Receive(TestBridge.Call(id, "echo", "back", ""));
        }

        Assert.Equal(30, _durations.Count);
        Assert.Equal(20, _activities.Count);
        Assert.Equal(20, bridge.Log.Entries.Count(e => e.Message.Contains("was refused (contract-mismatch)", StringComparison.Ordinal)));

        time.Advance(TimeSpan.FromSeconds(1));
        bridge.Session.Receive(TestBridge.Call(31, "echo", "back", ""));

        Assert.Equal(21, _activities.Count);
        Assert.Contains(bridge.Log.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information
            && e.Message.Contains("10 refused calls of document doc-1 were not traced or logged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_result_over_the_limit_is_recorded_as_the_failure_the_page_gets()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { MaxResultBytes = 10 });

        bridge.Session.Receive(TestBridge.Call(1, "echo", "back", "\"longer than ten bytes\""));
        await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        Assert.Equal("response", Assert.Single(_activities).GetTagItem("spar.outcome"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never became true.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A stream message carrying this class's trace, so a test reads only its own spans.</summary>
    private static string Traced(string stream) => stream.Replace("\"credits\"", $"\"traceparent\":\"{TraceParent}\",\"credits\"", StringComparison.Ordinal);
}
