using System.ComponentModel;
using System.Text.Json;

namespace Spar.Runtime;

/// <summary>
/// The blobs of one result being written, for generated handlers whose result holds a <see cref="SparBlob"/>: each
/// one written becomes a handle of the call's document, and a write that fails, or passes the document's limits,
/// releases the handles it made. The write is synchronous, so the scope is the writing thread's.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparBlobWrite : IDisposable
{
    [ThreadStatic]
    private static BlobOwner? t_owner;

    [ThreadStatic]
    private static List<string>? t_added;

    private readonly BlobOwner _owner;
    private List<string>? _added;

    private SparBlobWrite(BlobOwner owner, List<string> added)
    {
        _owner = owner;
        _added = added;
    }

    /// <summary>Starts writing a result of <paramref name="call"/>; dispose it after the write.</summary>
    public static SparBlobWrite Begin(SparCallContext call)
    {
        var owner = call.BlobOwner ?? throw new InvalidOperationException(
            "This call has no document to hold a SparBlob: a blob can be returned to a page, or to a test through SparTestApp.");
        var added = new List<string>();
        t_owner = owner;
        t_added = added;
        return new SparBlobWrite(owner, added);
    }

    /// <summary>Keeps the handles: the result was written and goes to the page.</summary>
    public void Commit() => _added = null;

    public void Dispose()
    {
        t_owner = null;
        t_added = null;
        if (_added is null)
        {
            return;
        }

        foreach (var id in _added)
        {
            _owner.Release(id);
        }

        _added = null;
    }

    /// <summary>
    /// Writes a blob as its handle, <c>{"id":…,"contentType":…,"size":…,"name":…}</c>, adding it to the document's
    /// handles; <c>name</c> is left out when there is none.
    /// </summary>
    public static void Write(Utf8JsonWriter writer, SparBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var owner = t_owner ?? throw new InvalidOperationException("A SparBlob can be written only in a command's result.");
        var id = owner.Add(blob);
        t_added!.Add(id);

        writer.WriteStartObject();
        writer.WriteString("id"u8, id);
        writer.WriteString("contentType"u8, blob.ContentType);
        writer.WriteNumber("size"u8, blob.Length);
        if (blob.Name is { } name)
        {
            writer.WriteString("name"u8, name);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads a handle, as a typed test client receives it: the blob it refers to, when the handle is in the test app's
    /// store (the test host lists its apps' stores); it reads as <see cref="SparBlob.OpenRead"/> does on the backend.
    /// </summary>
    public static SparBlob Read(ref Utf8JsonReader reader)
    {
        SparJson.Expect(ref reader, JsonTokenType.StartObject);
        string? id = null;
        while (SparJson.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("id"u8))
            {
                reader.Read();
                id = SparJson.ReadString(ref reader);
            }
            else
            {
                SparJson.SkipValue(ref reader);
            }
        }

        if (id is null)
        {
            throw SparJson.Missing(ref reader, "id");
        }

        return BlobStore.FindForTest(id) ?? throw SparJson.Error(ref reader, $"No handle {id} in a test app of this process");
    }
}
