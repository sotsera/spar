using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Spar;

/// <summary>
/// Every handle the app's documents hold, by its 128-bit id (decision "M3b's decide-first questions", item 2). One per
/// app: the hosts answer <c>/_spar/blob/&lt;id&gt;</c> from it without knowing which document asked, since the id is
/// unguessable; each handle belongs to one <see cref="BlobOwner"/>, a document, which counts its limits and releases what
/// is left when it goes.
/// </summary>
internal sealed class BlobStore
{
    /// <summary>The stores of the test host's apps in this process, where a typed test client finds the handles it reads.</summary>
    private static readonly ConcurrentDictionary<BlobStore, byte> TestStores = new();

    private readonly ConcurrentDictionary<string, Handle> _handles = new(StringComparer.Ordinal);

    /// <summary>How many handles every owner holds together; for tests.</summary>
    public int Count => _handles.Count;

    /// <summary>Lists this store for the typed test clients of this process, until <see cref="EndTest"/>.</summary>
    public void StartTest() => TestStores[this] = 0;

    public void EndTest() => TestStores.TryRemove(this, out _);

    /// <summary>The blob of a handle in any test app's store; handle ids are unique, so no two stores share one.</summary>
    public static SparBlob? FindForTest(string id)
    {
        foreach (var store in TestStores.Keys)
        {
            if (store.Find(id) is { } blob)
            {
                return blob;
            }
        }

        return null;
    }

    /// <summary>A new owner, for one document (or the test host's calls), with the app's limits.</summary>
    public BlobOwner Open(long maxBytes, int maxHandles) => new(this, maxBytes, maxHandles);

    /// <summary>The blob a handle refers to, or null when there is no such handle (never made, released, or its document gone).</summary>
    public SparBlob? Find(string id) => _handles.TryGetValue(id, out var handle) ? handle.Blob : null;

    /// <summary>The blob, when the handle is <paramref name="owner"/>'s; for a host that knows the document that asked.</summary>
    public SparBlob? Find(string id, BlobOwner owner) =>
        _handles.TryGetValue(id, out var handle) && handle.Owner == owner ? handle.Blob : null;

    internal string Add(SparBlob blob, BlobOwner owner)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        blob.AddHandle();
        _handles[id] = new Handle(blob, owner);
        return id;
    }

    internal SparBlob? Remove(string id, BlobOwner owner)
    {
        if (!_handles.TryGetValue(id, out var handle) || handle.Owner != owner || !_handles.TryRemove(id, out _))
        {
            return null;
        }

        handle.Blob.RemoveHandle();
        return handle.Blob;
    }

    private sealed record Handle(SparBlob Blob, BlobOwner Owner);
}

/// <summary>
/// The handles of one document: at most <c>maxHandles</c>, and at most <c>maxBytes</c> of <c>byte[]</c> data; a result
/// that would pass either fails its own call (<see cref="BlobLimitException"/>), and nothing is released to make room.
/// </summary>
internal sealed class BlobOwner
{
    private readonly BlobStore _store;
    private readonly long _maxBytes;
    private readonly int _maxHandles;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private long _bytes;
    private bool _closed;

    /// <summary>The window the document belongs to, whose grants and dialogs its calls use; null for none.</summary>
    public Files.HostWindow? Window { get; set; }

    internal BlobOwner(BlobStore store, long maxBytes, int maxHandles)
    {
        _store = store;
        _maxBytes = maxBytes;
        _maxHandles = maxHandles;
    }

    /// <summary>How many handles it holds.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _ids.Count;
            }
        }
    }

    /// <summary>How many bytes its <c>byte[]</c> handles hold.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>A new handle to <paramref name="blob"/>, or a <see cref="BlobLimitException"/> past a limit.</summary>
    public string Add(SparBlob blob)
    {
        var size = blob.InMemory ? blob.Length : 0;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_ids.Count >= _maxHandles)
            {
                throw new BlobLimitException($"the document holds {_ids.Count} handles; the limit is {_maxHandles}");
            }

            if (_bytes + size > _maxBytes)
            {
                throw new BlobLimitException(
                    $"the document's byte[] handles would hold {_bytes + size} bytes; the limit is {_maxBytes}");
            }

            var id = _store.Add(blob, this);
            _ids.Add(id);
            _bytes += size;
            return id;
        }
    }

    /// <summary>Releases one handle; false when it is not this owner's (already released, or never made).</summary>
    public bool Release(string id)
    {
        lock (_gate)
        {
            if (!_ids.Remove(id))
            {
                return false;
            }

            if (_store.Remove(id, this) is { InMemory: true } blob)
            {
                _bytes -= blob.Length;
            }

            return true;
        }
    }

    /// <summary>Releases every handle, when the document goes; later additions fail.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            foreach (var id in _ids)
            {
                _store.Remove(id, this);
            }

            _ids.Clear();
            _bytes = 0;
        }
    }
}

/// <summary>A result whose handles would pass the document's limits; its call fails with <c>response</c> / <c>too-large</c>.</summary>
internal sealed class BlobLimitException(string message) : Exception(message);
