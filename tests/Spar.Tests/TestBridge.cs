using Spar.Bridge;

namespace Spar.Tests;

/// <summary>An opened session over a backend with the given limits, its sent messages and its log.</summary>
internal sealed class TestBridge : IAsyncDisposable
{
    public const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\",\"event\",\"closing\"],\"contracts\":\"0123456789abcdef\"}\t";

    private TestBridge(TestBackend backend, CapturedLog log, bool includeDetails)
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

    public static async Task<TestBridge> OpenAsync(BridgeOptions? limits = null, bool includeDetails = false)
    {
        var log = new CapturedLog();
        var bridge = new TestBridge(TestBackend.Create(limits, log), log, includeDetails);
        bridge.Session.Receive(Hello);
        Assert.Equal(BridgeMessageType.Open, (await bridge.Sent.NextAsync()).Type);
        return bridge;
    }

    /// <summary>A call message, with a <c>traceparent</c> when one is given.</summary>
    public static string Call(long id, string contract, string command, string payload, string? traceParent = null)
    {
        var trace = traceParent is null ? "" : $",\"traceparent\":\"{traceParent}\"";
        return $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"{command}\"{trace}}}\t{payload}";
    }

    public async ValueTask DisposeAsync()
    {
        Session.Dispose();
        await Backend.DisposeAsync();
    }
}
