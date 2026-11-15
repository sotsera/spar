using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Spar.Bridge;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>A dispatcher over hand-written commands, standing in for what the generator writes.</summary>
internal sealed class TestBackend : IAsyncDisposable
{
    public const string Fingerprint = "0123456789abcdef";

    private readonly SparRunningApp _app;

    private TestBackend(SparRunningApp app)
    {
        _app = app;
    }

    public SparDispatcher Dispatcher => _app.Dispatcher;

    /// <summary>Calls that have started, so a test knows a command is running before it cancels it.</summary>
    public ConcurrentQueue<string> Started { get; } = new();

    public static TestBackend Create()
    {
        TestBackend? backend = null;
        var services = new ServiceCollection();
        var catalog = new SparCatalog(
            Fingerprint,
            [
                new("pages", "get", 0, call => Get(call)),
                new("slow", "wait", 0, async call =>
                {
                    backend!.Started.Enqueue("slow.wait");
                    await Task.Delay(Timeout.Infinite, call.CancellationToken);
                    return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
                }),
                new("slow", "limited", 1, async call =>
                {
                    await Task.Delay(Timeout.Infinite, call.CancellationToken);
                    return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
                }),
                new("boom", "throw", 0, _ => throw new IOException("disk full")),
                new("boom", "otherToken", 0, _ => throw new OperationCanceledException(new CancellationToken(canceled: true))),
                new("call", "describe", 0, call =>
                {
                    var current = call.Services.GetRequiredService<ISparCall>();
                    return ValueTask.FromResult(Json($"\"{current.Contract}.{current.Command}#{current.CallId}\""));
                }),
            ],
            _ => { });

        var folders = SparFolders.ForTest(Path.Combine(Path.GetTempPath(), "spar-tests-unused"));
        SparRunningApp.AddSparServices(services, catalog, folders);
        backend = new TestBackend(SparRunningApp.Build(services, catalog, folders));
        return backend;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    public static SparOutcome Json(string json) => SparOutcome.FromValue(Encoding.UTF8.GetBytes(json));

    private static ValueTask<SparOutcome> Get(SparCallContext call)
    {
        var request = Encoding.UTF8.GetString(call.Request.Span);
        var outcome = request == "{\"id\":\"missing\"}"
            ? SparOutcome.FromError(Encoding.UTF8.GetBytes("{\"code\":\"notFound\",\"id\":\"missing\"}"))
            : Json("{\"id\":\"ideas\",\"title\":\"Ideas\"}");
        return ValueTask.FromResult(outcome);
    }
}

/// <summary>What a session sent to its page, in order, with a way to wait for the next message.</summary>
internal sealed class SentMessages
{
    private readonly ConcurrentQueue<string> _all = new();
    private readonly System.Threading.Channels.Channel<string> _unread = System.Threading.Channels.Channel.CreateUnbounded<string>();

    public IReadOnlyList<string> All => [.. _all];

    public void Add(string message)
    {
        _all.Enqueue(message);
        _unread.Writer.TryWrite(message);
    }

    public async Task<BridgeMessage> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var text = await _unread.Reader.ReadAsync(timeout.Token);
        Assert.True(BridgeCodec.TryDecode(text, out var message, out var error), error?.Reason);
        return message!;
    }

    /// <summary>True when nothing more arrives within <paramref name="wait"/>.</summary>
    public async Task<bool> StaysQuietAsync(TimeSpan wait)
    {
        using var timeout = new CancellationTokenSource(wait);
        try
        {
            await _unread.Reader.WaitToReadAsync(timeout.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
