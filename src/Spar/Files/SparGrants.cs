using System.ComponentModel;
using System.Text.Json;
using Spar.Files;

namespace Spar.Runtime;

/// <summary>
/// Grants on the wire, for generated serializers: <c>{"id","name","size","access"}</c> for a file and
/// <c>{"id","name","access"}</c> for a folder, <c>access</c> being <c>read</c> or <c>readWrite</c>. A request's grant is
/// read by its id alone and resolved to the call's window's own grant (<see cref="Begin"/>), so an id the page made up,
/// one of another window, or a revoked one fails the request as <c>rejected</c> / <c>invalid-request</c> before the
/// command runs. The read is synchronous, so the scope is the reading thread's.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparGrants : IDisposable
{
    [ThreadStatic]
    private static GrantTable? t_table;

    [ThreadStatic]
    private static bool t_active;

    private SparGrants()
    {
    }

    /// <summary>Starts reading a request of <paramref name="call"/> that can hold grants; dispose it after the read.</summary>
    public static SparGrants Begin(SparCallContext call)
    {
        t_table = call.Window?.Grants;
        t_active = true;
        return new SparGrants();
    }

    public void Dispose()
    {
        t_table = null;
        t_active = false;
    }

    public static FileGrant ReadFile(ref Utf8JsonReader reader) =>
        Find(ref reader) as FileGrant ?? throw SparJson.Error(ref reader, "The grant is not a file grant of this window");

    public static FolderGrant ReadFolder(ref Utf8JsonReader reader) =>
        Find(ref reader) as FolderGrant ?? throw SparJson.Error(ref reader, "The grant is not a folder grant of this window");

    public static void Write(Utf8JsonWriter writer, FileGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        writer.WriteStartObject();
        writer.WriteString("id"u8, grant.Id);
        writer.WriteString("name"u8, grant.Name);
        writer.WriteNumber("size"u8, grant.Size);
        writer.WriteString("access"u8, grant.CanWrite ? "readWrite"u8 : "read"u8);
        writer.WriteEndObject();
    }

    public static void Write(Utf8JsonWriter writer, FolderGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        writer.WriteStartObject();
        writer.WriteString("id"u8, grant.Id);
        writer.WriteString("name"u8, grant.Name);
        writer.WriteString("access"u8, grant.Access == FolderAccess.ReadWrite ? "readWrite"u8 : "read"u8);
        writer.WriteEndObject();
    }

    /// <summary>
    /// The grant an object's <c>id</c> names: in a request, the call's window's; outside one (a typed test client
    /// reading a result), the test app's.
    /// </summary>
    private static object? Find(ref Utf8JsonReader reader)
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

        var grant = t_active ? t_table?.Find(id) : GrantTable.FindForTest(id);
        return grant is FileGrant { IsValid: true } or FolderGrant { IsValid: true } ? grant : null;
    }
}
