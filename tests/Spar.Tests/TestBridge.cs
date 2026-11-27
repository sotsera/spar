using Spar.Bridge;

namespace Spar.Tests;

/// <summary>An opened session over a backend with the given limits, its sent messages and its log.</summary>
internal sealed class TestBridge : IAsyncDisposable
{
    public const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\",\"event\",\"closing\"],\"contracts\":\"0123456789abcdef\"}\t";

    private TestBridge(TestBackend backend, CapturedLog log, bool includeDetails, TimeProvider? time, Spar.Files.HostWindow? window)
    {
        Backend = backend;
        Log = log;
        Session = new BridgeSession(backend.Dispatcher, TestBackend.Fingerprint, "doc-1", Sent.Add, includeDetails, time) { Window = window };
        Session.Dropped += reason => { lock (Dropped) { Dropped.Add(reason); } };
    }

    public TestBackend Backend { get; }

    public CapturedLog Log { get; }

    public BridgeSession Session { get; }

    public SentMessages Sent { get; } = new();

    public List<string> Dropped { get; } = [];

    public static async Task<TestBridge> OpenAsync(
        BridgeOptions? limits = null, bool includeDetails = false, TimeProvider? time = null, string hello = Hello, Spar.Files.HostWindow? window = null)
    {
        var log = new CapturedLog();
        var bridge = new TestBridge(TestBackend.Create(limits, log), log, includeDetails, time, window);
        bridge.Session.Receive(hello);
        Assert.Equal(BridgeMessageType.Open, (await bridge.Sent.NextAsync()).Type);
        return bridge;
    }

    /// <summary>A call message, with a <c>traceparent</c> when one is given.</summary>
    public static string Call(long id, string contract, string command, string payload, string? traceParent = null)
    {
        var trace = traceParent is null ? "" : $",\"traceparent\":\"{traceParent}\"";
        return $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"{command}\"{trace}}}\t{payload}";
    }

    /// <summary>
    /// Closes the session and waits for its commands to end, so none of them finishes during a later test: a span that
    /// stops then would reach that test's listener, since activity listeners are process-wide.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Session.Close();
        await Session.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Session.Dispose();
        await Backend.DisposeAsync();
    }
}

/// <summary>A clock that moves only when the test moves it.</summary>
internal sealed class ManualTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
