namespace Sotsera.Spar.Bridge;

/// <summary>A generated contract plus a host-owned, explicit permission grant set.</summary>
public sealed class SparBridge
{
    public const int MaximumMessageBytes = 64 * 1024;
    public const int MaximumConcurrentCalls = 8;
    public const int MaximumTimeoutMilliseconds = 30_000;

    private readonly ISparBridgeDispatcher _dispatcher;
    private readonly HashSet<string> _permissions;
    private int _pending;

    public SparBridge(ISparBridgeDispatcher dispatcher, IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(permissions);
        if (dispatcher.Fingerprint.Length != 64 || !dispatcher.Fingerprint.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("The generated contract must supply a SHA-256 fingerprint.", nameof(dispatcher));
        }

        _dispatcher = dispatcher;
        _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
    }

    /// <summary>The native transport must supply the actual source and navigation ID, never renderer claims.</summary>
    public SparBridgeDocument OpenDocument(string source, ulong navigationId, Action<SparBridgeEvent>? observer = null,
        string? clientCredential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (clientCredential is not null && (clientCredential.Length != 64 || !clientCredential.All(char.IsAsciiHexDigit)))
        {
            throw new ArgumentException("A client credential must contain 32 random bytes encoded as 64 hex characters.",
                nameof(clientCredential));
        }

        return new SparBridgeDocument(this, _dispatcher, source, navigationId, observer, clientCredential);
    }

    internal bool HasPermission(string permission)
    {
        return _permissions.Contains(permission);
    }

    internal bool TryAcquireCall()
    {
        if (Interlocked.Increment(ref _pending) <= MaximumConcurrentCalls)
        {
            return true;
        }

        Interlocked.Decrement(ref _pending);
        return false;
    }

    internal void ReleaseCall()
    {
        Interlocked.Decrement(ref _pending);
    }
}
