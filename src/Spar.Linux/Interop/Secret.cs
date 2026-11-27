using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// libsecret, the Secret Service's client library (checked against the 0.21 headers of Ubuntu 24.04), through its
/// non-variadic calls (<c>…v_sync</c>), which take their attributes as a <c>GHashTable</c> of UTF-8 strings. Each call
/// talks to the service over the session bus and waits, so it runs off the main loop. Loaded at the first call: an app
/// that never stores a credential never needs the library.
/// </summary>
internal static unsafe partial class Secret
{
    private const string Library = "libsecret-1.so.0";
    private const string Glib = "libglib-2.0.so.0";

    /// <summary><c>SECRET_COLLECTION_DEFAULT</c>: the user's default keyring.</summary>
    public const string DefaultCollection = "default";

    /// <summary>A schema named <paramref name="name"/> with the attributes the table lists (name to type, 0 for string); owned.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint secret_schema_newv(string name, int flags, nint attributeNamesAndTypes);

    [LibraryImport(Library)]
    public static partial void secret_schema_unref(nint schema);

    /// <summary>Stores a password; false with an owned <c>GError</c> on failure.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int secret_password_storev_sync(
        nint schema, nint attributes, string collection, string label, string password, nint cancellable, nint* error);

    /// <summary>The password, owned (<see cref="secret_password_free"/>); 0 when none matched, or with an owned <c>GError</c>.</summary>
    [LibraryImport(Library)]
    public static partial nint secret_password_lookupv_sync(nint schema, nint attributes, nint cancellable, nint* error);

    /// <summary>Clears the password from memory before freeing it.</summary>
    [LibraryImport(Library)]
    public static partial void secret_password_free(nint password);

    /// <summary>True when one was removed, false when none matched or with an owned <c>GError</c>.</summary>
    [LibraryImport(Library)]
    public static partial int secret_password_clearv_sync(nint schema, nint attributes, nint cancellable, nint* error);

    /// <summary>A hash table of strings, keyed by <c>g_str_hash</c> and <c>g_str_equal</c>; owned (<see cref="g_hash_table_unref"/>).</summary>
    [LibraryImport(Glib)]
    public static partial nint g_hash_table_new(nint hash, nint equal);

    [LibraryImport(Glib)]
    public static partial int g_hash_table_insert(nint table, nint key, nint value);

    [LibraryImport(Glib)]
    public static partial void g_hash_table_unref(nint table);

    /// <summary>GLib's string hash and equality, for <see cref="g_hash_table_new"/>.</summary>
    public static (nint Hash, nint Equal) StringFunctions()
    {
        var glib = NativeLibrary.Load(Glib);
        return (NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
    }
}
