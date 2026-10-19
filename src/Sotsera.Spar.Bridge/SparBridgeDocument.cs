using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sotsera.Spar.Bridge;

/// <summary>Admission and execution ownership for one native document. Retirement never implies task completion.</summary>
public sealed class SparBridgeDocument
{
    private readonly object _gate = new();
    private readonly SparBridge _bridge;
    private readonly ISparBridgeDispatcher _dispatcher;
    private readonly Action<SparBridgeEvent>? _observer;
    private readonly string? _clientCredential;
    private readonly Dictionary<int, BridgeCall> _calls = [];
    private string? _client;
    private int _highestRequestId;
    private bool _active = true;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string Source
    {
        get;
    }

    public ulong NavigationId
    {
        get;
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    internal SparBridgeDocument(SparBridge bridge, ISparBridgeDispatcher dispatcher, string source, ulong navigationId,
        Action<SparBridgeEvent>? observer, string? clientCredential)
    {
        _bridge = bridge;
        _dispatcher = dispatcher;
        Source = source;
        NavigationId = navigationId;
        _observer = observer;
        _clientCredential = clientCredential;
    }

    /// <summary>Returns work only for an admitted call. Its task ends after the handler AND cancellation callbacks end.</summary>
    /// <remarks>The send callback queues a reply; it does not prove renderer delivery. The native transport must recheck identity.</remarks>
    public Task? Dispatch(string nativeSource, string message, Action<string> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        if (!string.Equals(nativeSource, Source, StringComparison.Ordinal) || !IsActive)
        {
            Observe("rejected", 0, null, "document_rejected");
            return null;
        }

        BridgeRequest request;
        try
        {
            if (message.Length > SparBridge.MaximumMessageBytes || Encoding.UTF8.GetByteCount(message) > SparBridge.MaximumMessageBytes)
            {
                throw new JsonException("Message exceeds the input limit.");
            }

            using var parsed = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 16 });
            CheckMembers(parsed.RootElement);
            request = JsonSerializer.Deserialize(parsed.RootElement, BridgeWireContext.Default.BridgeRequest)
                ?? throw new JsonException("Null envelope.");
            if (request.Version != 1 || request.Client.Length != 32 || !request.Client.All(char.IsAsciiHexDigit)
                || request.Kind.Length > 16 || request.Contract.Length > 64 || request.Operation?.Length > 64
                || request.Document?.Length > 32)
            {
                throw new JsonException("Invalid envelope fields.");
            }
        }
        catch (JsonException)
        {
            Observe("rejected", 0, null, "invalid_message");
            return null;
        }

        lock (_gate)
        {
            if (!_active)
            {
                Observe("rejected", request.Id, request.Operation, "document_rejected");
                return null;
            }

            if (!string.Equals(request.Contract, _dispatcher.Fingerprint, StringComparison.Ordinal))
            {
                Reject(request, "contract_mismatch", send);
                return null;
            }

            if (request.Kind == "hello" && request.Id == 0)
            {
                if (_clientCredential is not null && (request.Payload.ValueKind != JsonValueKind.String
                    || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_clientCredential),
                        Encoding.UTF8.GetBytes(request.Payload.GetString()!))))
                {
                    Reject(request, "client_unauthorized", send);
                    return null;
                }

                if (_client is not null && _client != request.Client)
                {
                    Reject(request, "client_already_bound", send);
                    return null;
                }

                _client = request.Client;
                Reply(request, "hello", null, null, send);
                Observe("connected", 0, null, null);
                return null;
            }

            if (request.Document != Id || request.Client != _client)
            {
                Reject(request, "document_rejected", send);
                return null;
            }

            if (request.Kind == "cancel")
            {
                if (_calls.TryGetValue(request.Id, out var pending))
                {
                    pending.RequestCancellation();
                }

                return null;
            }

            if (request.Kind != "call" || request.Id <= _highestRequestId || request.Id <= 0)
            {
                Reject(request, "invalid_request_id", send);
                return null;
            }

            _highestRequestId = request.Id;
            if (request.Operation is null || !_dispatcher.TryGetPermission(request.Operation, out var permission))
            {
                Reject(request, "unknown_operation", send);
                return null;
            }

            if (!_bridge.HasPermission(permission))
            {
                Reject(request, "permission_denied", send);
                return null;
            }

            if (request.TimeoutMs is < 1 or > SparBridge.MaximumTimeoutMilliseconds)
            {
                Reject(request, "invalid_timeout", send);
                return null;
            }

            if (!_bridge.TryAcquireCall())
            {
                Reject(request, "busy", send);
                return null;
            }

            var call = new BridgeCall();
            _calls.Add(request.Id, call);
            Observe("admitted", request.Id, request.Operation, null);
            return RunCallAsync(request, call, send);
        }
    }

    public void Retire()
    {
        BridgeCall[] calls;
        lock (_gate)
        {
            _active = false;
            calls = [.. _calls.Values];
        }

        foreach (var call in calls)
        {
            call.RequestCancellation();
        }
    }

    private async Task RunCallAsync(BridgeRequest request, BridgeCall call, Action<string> send)
    {
        using var deadline = new CancellationTokenSource();
        Task<JsonElement>? invocation = null;
        try
        {
            // User code never runs on the native STA, even before its first await.
            invocation = Task.Run(async () =>
                await _dispatcher.InvokeAsync(request.Operation!, request.Payload, call.Token).ConfigureAwait(false));
            var elapsed = Task.Delay(request.TimeoutMs ?? SparBridge.MaximumTimeoutMilliseconds, deadline.Token);
            var winner = await Task.WhenAny(invocation, elapsed, call.CancellationRequested).ConfigureAwait(false);
            if (winner != invocation)
            {
                call.RequestCancellation();
                Reject(request, winner == elapsed ? "timeout" : "cancelled", send);
            }
            else
            {
                var result = await invocation.ConfigureAwait(false);
                Reply(request, "result", call.Token.IsCancellationRequested ? "cancelled" : null, result, send);
            }
        }
        catch (SparPayloadException)
        {
            Reject(request, "invalid_payload", send);
        }
        catch (OperationCanceledException) when (call.Token.IsCancellationRequested)
        {
            Reject(request, "cancelled", send);
        }
        catch (Exception)
        {
            Reject(request, "handler_failed", send);
        }
        finally
        {
            if (invocation is not null)
            {
                try
                {
                    await invocation.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A wire outcome was already emitted. Still observe actual completion; never replay the call.
                }
            }

            await deadline.CancelAsync().ConfigureAwait(false);
            await call.DisposeAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _calls.Remove(request.Id);
                _bridge.ReleaseCall();
            }

            Observe("completed", request.Id, request.Operation, null);
        }
    }

    private void Reject(BridgeRequest request, string code, Action<string> send)
    {
        Observe("rejected", request.Id, request.Operation, code);
        Reply(request, request.Kind == "hello" ? "hello" : "result", code, null, send);
    }

    private void Reply(BridgeRequest request, string kind, string? code, JsonElement? result, Action<string> send)
    {
        lock (_gate)
        {
            if (!_active)
            {
                Observe("reply_suppressed", request.Id, request.Operation, code);
                return;
            }

            var reply = new BridgeReply(1, kind, request.Id, _dispatcher.Fingerprint, request.Client,
                Id, code is null ? "ok" : "error", code, code is null ? result : null);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, BridgeWireContext.Default.BridgeReply);
            if (bytes.Length > SparBridge.MaximumMessageBytes)
            {
                reply = reply with
                {
                    Status = "error",
                    Code = "output_too_large",
                    Result = null
                };
                bytes = JsonSerializer.SerializeToUtf8Bytes(reply, BridgeWireContext.Default.BridgeReply);
            }

            send(Encoding.UTF8.GetString(bytes));
        }
    }

    private void Observe(string stage, int id, string? operation, string? code)
    {
        try
        {
            _observer?.Invoke(new SparBridgeEvent(stage, Id, id, operation, code));
        }
        catch (Exception)
        {
            // Diagnostic observers cannot abandon ownership or change a call's wire outcome.
        }
    }

    private static void CheckMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in value.EnumerateObject())
            {
                if (names.Count >= 32 || !names.Add(member.Name))
                {
                    throw new JsonException("Duplicate or excessive object members.");
                }

                CheckMembers(member.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                CheckMembers(item);
            }
        }
    }
}
