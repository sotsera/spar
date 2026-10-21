using System.Collections.Concurrent;
using System.Text.Json;
using AwesomeAssertions;

namespace Sotsera.Spar.Bridge.Tests;

public sealed class BridgeTests
{
    [Theory]
    [InlineData("unknown_operation", "missing", null)]
    [InlineData("permission_denied", "denied", null)]
    [InlineData("contract_mismatch", "change", "contract")]
    [InlineData("document_rejected", "change", "document")]
    [InlineData("document_rejected", "change", "source")]
    [InlineData("invalid_message", "change", "malformed")]
    [InlineData("invalid_message", "change", "oversized")]
    [InlineData("invalid_message", "change", "duplicate")]
    [InlineData("invalid_payload", "change", "payload")]
    [InlineData("invalid_payload", "change", "null-field")]
    [InlineData("invalid_payload", "change", "extra-field")]
    [InlineData("invalid_payload", "change", "long-field")]
    public async Task RejectionPreventsHandlerEntry(string code, string operation, string? variation)
    {
        var session = new Session();
        var message = session.Message(1, operation);
        var source = Session.Source;
        switch (variation)
        {
            case "contract":
                message = message.Replace(session.Dispatcher.Fingerprint, new string('0', 64), StringComparison.Ordinal);
                break;
            case "document":
                message = message.Replace(session.Document.Id, new string('0', 32), StringComparison.Ordinal);
                break;
            case "source":
                source = "https://attacker.invalid/";
                break;
            case "malformed":
                message = "{broken";
                break;
            case "oversized":
                message = new string(' ', 66000);
                break;
            case "duplicate":
                message = message.Replace("\"value\":1", "\"value\":1,\"value\":2", StringComparison.Ordinal);
                break;
            case "payload":
                message = session.Message(1, operation, payload: "{\"name\":\"test\",\"value\":\"bad\"}");
                break;
            case "null-field":
                message = session.Message(1, operation, payload: "{\"name\":null,\"value\":1}");
                break;
            case "extra-field":
                message = session.Message(1, operation, payload: "{\"name\":\"test\",\"value\":1,\"other\":true}");
                break;
            case "long-field":
                message = session.Message(1, operation, payload: "{\"name\":\"" + new string('x', 4097) + "\",\"value\":1}");
                break;
        }

        var work = session.Document.Dispatch(source, message, session.Replies.Enqueue);
        if (work is not null)
        {
            await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        session.Backend.Entries.Should().Be(0);
        session.Events.Should().Contain(item => item.Stage == "rejected" && item.Code == code);
    }

    [Fact]
    public async Task MutationIsNeverReplayedForRepeatedRequestId()
    {
        var session = new Session();
        var message = session.Message(1, "change");
        await session.Document.Dispatch(Session.Source, message, session.Replies.Enqueue)!;
        session.Document.Dispatch(Session.Source, message, session.Replies.Enqueue).Should().BeNull();
        session.Backend.Entries.Should().Be(1);
        session.Events.Should().Contain(item => item.Code == "invalid_request_id");
    }

    [Fact]
    public async Task TimeoutRetainsAllSlotsUntilActualWorkFinishes()
    {
        var session = new Session();
        var work = Enumerable.Range(1, 8).Select(id => session.Document.Dispatch(Session.Source,
            session.Message(id, "wait", timeout: 30), session.Replies.Enqueue)!).ToArray();
        try
        {
            await UntilAsync(() => session.Events.Count(item => item.Code == "timeout") == 8);
            work.Should().OnlyContain(task => !task.IsCompleted);
            session.Document.Dispatch(Session.Source, session.Message(9, "change"), session.Replies.Enqueue).Should().BeNull();
            session.Events.Should().Contain(item => item.Code == "busy");
        }
        finally
        {
            session.Backend.Release.TrySetResult();
            await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        await session.Document.Dispatch(Session.Source, session.Message(10, "change"), session.Replies.Enqueue)!;
        session.Backend.Entries.Should().Be(9);
    }

    [Fact]
    public async Task RetirementSuppressesReplyAndKeepsWorkOwned()
    {
        var session = new Session();
        var work = session.Document.Dispatch(Session.Source, session.Message(1, "wait"), session.Replies.Enqueue)!;
        try
        {
            await session.Backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            session.Document.Retire();
            work.IsCompleted.Should().BeFalse();
            session.Document.Dispatch(Session.Source, session.Message(2, "change"), session.Replies.Enqueue).Should().BeNull();
        }
        finally
        {
            session.Backend.Release.TrySetResult();
            await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        session.Replies.Should().BeEmpty();
        session.Events.Should().Contain(item => item.Stage == "reply_suppressed");
        session.Backend.Entries.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationCallbacksAreAlsoOwnedUntilTheyReturn(bool waitForCancellationReply)
    {
        var session = new Session();
        session.Backend.BlockCancellation = true;
        var work = session.Document.Dispatch(Session.Source, session.Message(1, "wait"), session.Replies.Enqueue)!;
        try
        {
            await session.Backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            session.Document.Dispatch(Session.Source, session.Message(1, null, kind: "cancel"), session.Replies.Enqueue).Should().BeNull();
            await session.Backend.CallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (waitForCancellationReply)
            {
                await UntilAsync(() => !session.Replies.IsEmpty);
            }
            session.Backend.Release.TrySetResult();
            await session.Backend.HandlerFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            work.IsCompleted.Should().BeFalse("cancellation callbacks still own application resources");
        }
        finally
        {
            session.Backend.Release.TrySetResult();
            session.Backend.CallbackRelease.TrySetResult();
            await work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        // Both the cancellation-first and invocation-result paths return a cancelled wire reply.
        // Only cancellation-first also emits a rejected event; callback ownership must not depend on that race.
        session.Replies.Should().ContainSingle();
        using var reply = JsonDocument.Parse(session.Replies.Single());
        reply.RootElement.GetProperty("status").GetString().Should().Be("error");
        reply.RootElement.GetProperty("code").GetString().Should().Be("cancelled");
        session.Events.Should().Contain(item => item.Stage == "completed");
        session.Backend.Entries.Should().Be(1);
    }

    [Theory]
    [InlineData(false, "output_too_large")]
    [InlineData(true, "handler_failed")]
    public async Task RepliesAreBoundedAndDoNotExposeHandlerExceptions(bool fail, string code)
    {
        using var result = JsonDocument.Parse("\"" + new string('x', SparBridge.MaximumMessageBytes) + "\"");
        var session = new Session(new ResultDispatcher(result.RootElement.Clone(), fail));
        await session.Document.Dispatch(Session.Source, session.Message(1, "change"), session.Replies.Enqueue)!;
        session.Replies.Should().ContainSingle();
        var reply = session.Replies.Single();
        System.Text.Encoding.UTF8.GetByteCount(reply).Should().BeLessThanOrEqualTo(SparBridge.MaximumMessageBytes);
        using var parsed = JsonDocument.Parse(reply);
        parsed.RootElement.GetProperty("code").GetString().Should().Be(code);
        reply.Should().NotContain("private exception details");
    }

    [Fact]
    public async Task ALoopbackCapabilityMustBeProvedBeforeClientBindingOrHandlerEntry()
    {
        var credential = new string('b', 64);
        var session = new Session(clientCredential: credential);
        session.Events.Should().Contain(value => value.Code == "client_unauthorized");
        session.Document.Dispatch(Session.Source, session.Message(1, "change"), session.Replies.Enqueue).Should().BeNull();
        session.Backend.Entries.Should().Be(0);
        session.Document.Dispatch(Session.Source, session.Message(0, null, kind: "hello", payload: "\"" + credential + "\""),
            session.Replies.Enqueue).Should().BeNull();
        await session.Document.Dispatch(Session.Source, session.Message(2, "change"), session.Replies.Enqueue)!;
        session.Backend.Entries.Should().Be(1);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private sealed class Session
    {
        internal const string Source = "https://owned.spar.invalid/index.html";
        private const string Client = "11111111111111111111111111111111";
        internal Backend Backend { get; } = new();
        internal ISparBridgeDispatcher Dispatcher
        {
            get;
        }
        internal SparBridgeDocument Document
        {
            get;
        }
        internal ConcurrentQueue<SparBridgeEvent> Events { get; } = new();
        internal ConcurrentQueue<string> Replies { get; } = new();

        internal Session(ISparBridgeDispatcher? dispatcher = null, string? clientCredential = null)
        {
            Dispatcher = dispatcher ?? new TestBackendBridge(Backend);
            var bridge = new SparBridge(Dispatcher, ["counter.change", "work.run"]);
            Document = bridge.OpenDocument(Source, 1, Events.Enqueue, clientCredential);
            Document.Dispatch(Source, Message(0, null, kind: "hello"), _ => { }).Should().BeNull();
        }

        internal string Message(int id, string? operation, string kind = "call", int timeout = 5000,
            string payload = "{\"name\":\"test\",\"value\":1}")
        {
            return $$"""{"version":1,"kind":"{{kind}}","id":{{id}},"contract":"{{Dispatcher.Fingerprint}}","client":"{{Client}}","document":"{{Document.Id}}","operation":{{JsonSerializer.Serialize(operation)}},"payload":{{payload}},"timeoutMs":{{timeout}}}""";
        }
    }

    private sealed class ResultDispatcher(JsonElement result, bool fail) : ISparBridgeDispatcher
    {
        public string Fingerprint => new string('a', 64);

        public bool TryGetPermission(string operation, out string permission)
        {
            permission = "counter.change";
            return true;
        }

        public ValueTask<JsonElement> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new InvalidOperationException("private exception details");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Backend : ITestBackend
    {
        private int _entries;
        internal int Entries => Volatile.Read(ref _entries);
        internal bool BlockCancellation
        {
            get; set;
        }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CallbackStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CallbackRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource HandlerFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TestReply> ChangeAsync(TestRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TestReply(request.Name, Interlocked.Increment(ref _entries)));
        }

        public async Task<TestReply> WaitAsync(TestRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _entries);
            // Deliberately retained until cancellation callbacks finish; disposing it here would join a blocked callback.
            if (BlockCancellation)
            {
                cancellationToken.Register(() =>
                {
                    CallbackStarted.TrySetResult();
                    CallbackRelease.Task.GetAwaiter().GetResult();
                });
            }

            Started.TrySetResult();
            await Release.Task;
            HandlerFinished.TrySetResult();
            return new TestReply(request.Name, request.Value);
        }

        public Task<TestReply> DeniedAsync(TestRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _entries);
            throw new InvalidOperationException("Denied handler must never enter.");
        }
    }
}
