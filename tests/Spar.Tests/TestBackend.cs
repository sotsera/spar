using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>The running app, for the devtools' tests.</summary>
    public SparRunningApp App => _app;

    /// <summary>Calls that have started, so a test knows a command is running before it cancels it.</summary>
    public ConcurrentQueue<string> Started { get; } = new();

    /// <summary>A backend with the default bridge limits, or <paramref name="limits"/>; <paramref name="log"/> receives its log.</summary>
    public static TestBackend Create(BridgeOptions? limits = null, CapturedLog? log = null)
    {
        TestBackend? backend = null;
        var services = new ServiceCollection();
        if (log is not null)
        {
            services.AddSingleton<ILoggerFactory>(log);
        }

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
                new("echo", "back", 0, call => ValueTask.FromResult(SparOutcome.FromValue(call.Request.ToArray()))),
                new("boom", "otherToken", 0, _ => throw new OperationCanceledException(new CancellationToken(canceled: true))),
                new("call", "describe", 0, call =>
                {
                    var current = call.Services.GetRequiredService<ISparCall>();
                    return ValueTask.FromResult(Json($"\"{current.Contract}.{current.Command}#{current.CallId}\""));
                }),
                new("call", "caller", 0, call =>
                    ValueTask.FromResult(Json($"\"{call.Services.GetRequiredService<ISparCall>().Caller}\""))),
            ],
            _ => { });

        var folders = SparFolders.ForTest(Path.Combine(Path.GetTempPath(), "spar-tests-unused"));
        SparRunningApp.AddSparServices(services, catalog, folders, (limits ?? new BridgeOptions()).Freeze());
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

/// <summary>A logger factory that keeps every entry, for tests that check what the bridge logs.</summary>
internal sealed class CapturedLog : ILoggerFactory, ILogger
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue((logLevel, formatter(state, exception), exception));

    public void Dispose()
    {
    }
}
