#if PART_GRANTS
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace NativeServices.App.Grants;

[Flags]
internal enum GrantAccess
{
    None = 0,
    Read = 1,
    Write = 2,
}

internal enum GrantError
{
    None,
    Unknown,   // never issued, revoked, or issued before a restart
    Expired,
    Forbidden, // the grant exists but lacks the operation
    TooLarge,
}

/// <summary>A backend-held file grant. The path never leaves the backend; the page sees only the handle and name.</summary>
internal sealed record FileGrant(string Path, GrantAccess Access, DateTimeOffset ExpiresAt)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

internal sealed class GrantException(GrantError error, string message) : Exception(message)
{
    public GrantError Error { get; } = error;
}

/// <summary>
/// Opaque random handle -> grant. In-memory only: a restart (or window close) drops every grant, and a stale handle
/// reads as Unknown, so the page must ask the user again.
/// </summary>
internal sealed class GrantTable(TimeProvider time)
{
    public const int MaxReadBytes = 16 * 1024 * 1024;
    private const string Prefix = "fg_";

    private readonly Dictionary<string, FileGrant> _grants = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

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

    /// <summary>Issues a grant for a path the user chose in a backend-owned dialog. Never for a page-supplied path.</summary>
    public string Issue(string path, GrantAccess access, TimeSpan lifetime)
    {
        var handle = NewHandle();
        var grant = new FileGrant(Path.GetFullPath(path), access, time.GetUtcNow() + lifetime);
        lock (_gate)
        {
            _grants.Add(handle, grant);
        }

        return handle;
    }

    public bool Revoke(string handle)
    {
        lock (_gate)
        {
            return _grants.Remove(handle);
        }
    }

    public void RevokeAll()
    {
        lock (_gate)
        {
            _grants.Clear();
        }
    }

    public FileGrant Require(string handle, GrantAccess needed)
    {
        FileGrant? grant;
        lock (_gate)
        {
            if (!_grants.TryGetValue(handle, out grant))
            {
                throw new GrantException(GrantError.Unknown, "The file handle is unknown or no longer valid.");
            }

            if (time.GetUtcNow() >= grant.ExpiresAt)
            {
                _grants.Remove(handle);
                throw new GrantException(GrantError.Expired, "The file handle has expired.");
            }
        }

        if ((grant.Access & needed) != needed)
        {
            throw new GrantException(GrantError.Forbidden, $"The file handle does not allow {needed}.");
        }

        return grant;
    }

    public string ReadText(string handle)
    {
        var grant = Require(handle, GrantAccess.Read);
        using var stream = new FileStream(grant.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxReadBytes)
        {
            throw new GrantException(GrantError.TooLarge, $"The file is larger than {MaxReadBytes} bytes.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Writes through a temporary file in the same folder, then replaces the target.</summary>
    public void WriteText(string handle, string text)
    {
        var grant = Require(handle, GrantAccess.Write);
        var directory = Path.GetDirectoryName(grant.Path)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(grant.Path)}.{Environment.ProcessId}.tmp");
        File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, grant.Path, overwrite: true);
    }

    private static string NewHandle()
    {
        Span<byte> random = stackalloc byte[16];
        RandomNumberGenerator.Fill(random);
        return Prefix + Base64Url.EncodeToString(random);
    }
}

/// <summary>A controllable clock for the expiry checks.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
#endif
