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
/// The always-on instrumentation of bridge commands (plan section 6b): one server activity per command, a child of the
/// page's <c>traceparent</c>, and one <c>spar.call.duration</c> sample, tagged with the command and its outcome.
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
    public async Task A_result_over_the_limit_is_recorded_as_the_failure_the_page_gets()
    {
        await using var bridge = await TestBridge.OpenAsync(new BridgeOptions { MaxResultBytes = 10 });

        bridge.Session.Receive(TestBridge.Call(1, "echo", "back", "\"longer than ten bytes\""));
        await bridge.Sent.NextAsync();
        await bridge.Session.WhenIdleAsync();

        Assert.Equal("response", Assert.Single(_activities).GetTagItem("spar.outcome"));
    }
}
