using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Spar.Files;

/// <summary>
/// One window's grants (decision "M3c's decide-first questions", item 2): ids of 128 random bits, <c>fg_…</c> for a
/// file and <c>fd_…</c> for a folder; at most <see cref="Capacity"/>, past which a new one is refused rather than an
/// old one revoked; all revoked together when the window closes. The page holds only ids: a request's id is resolved
/// here, so an unknown one, or another window's, is refused before the command runs.
/// </summary>
internal sealed class GrantTable(TimeProvider time, int capacity = GrantTable.DefaultCapacity)
{
    public const int DefaultCapacity = 1000;

    private static readonly ConcurrentDictionary<string, object> TestGrants = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, object> _grants = new(StringComparer.Ordinal);
    private bool _closed;

    public int Capacity { get; } = capacity;

    /// <summary>How many grants it holds.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _grants.Count;
            }
        }
    }

    /// <summary>Whether <paramref name="count"/> more grants fit.</summary>
    public bool HasRoom(int count)
    {
        lock (_gate)
        {
            return !_closed && _grants.Count + count <= Capacity;
        }
    }

    /// <summary>A grant on a file, or null when the table is full or closed.</summary>
    public FileGrant? AddFile(string path, bool canWrite, TimeSpan? expiry)
    {
        var grant = new FileGrant(this, NewId("fg_"), Path.GetFullPath(path), canWrite, ExpiresAt(expiry));
        return Add(grant.Id, grant) ? grant : null;
    }

    /// <summary>A grant on a folder, or null when the table is full or closed.</summary>
    public FolderGrant? AddFolder(string path, FolderAccess access, TimeSpan? expiry)
    {
        var grant = new FolderGrant(this, NewId("fd_"), path, access, ExpiresAt(expiry));
        return Add(grant.Id, grant) ? grant : null;
    }

    /// <summary>The grant of that id while it holds, or null.</summary>
    public object? Find(string id)
    {
        lock (_gate)
        {
            return _grants.GetValueOrDefault(id);
        }
    }

    /// <summary>Whether a grant of this table, with that expiry, still holds.</summary>
    public bool Holds(DateTimeOffset? expires)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }
        }

        return expires is not { } end || time.GetUtcNow() < end;
    }

    /// <summary>Revokes every grant: the window closed.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            foreach (var id in _grants.Keys)
            {
                TestGrants.TryRemove(id, out _);
            }

            _grants.Clear();
        }
    }

    /// <summary>
    /// A grant a typed test client reads from a result, found by its id in any window of this process; test runs only
    /// (<see cref="SparTestSession.IsSupported"/>).
    /// </summary>
    public static object? FindForTest(string id) => SparTestSession.IsSupported ? TestGrants.GetValueOrDefault(id) : null;

    private bool Add(string id, object grant)
    {
        lock (_gate)
        {
            if (_closed || _grants.Count >= Capacity)
            {
                return false;
            }

            _grants.Add(id, grant);
        }

        if (SparTestSession.IsSupported)
        {
            TestGrants[id] = grant;
        }

        return true;
    }

    private DateTimeOffset? ExpiresAt(TimeSpan? expiry) => expiry is { } span ? time.GetUtcNow() + span : null;

    private static string NewId(string prefix) => prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
