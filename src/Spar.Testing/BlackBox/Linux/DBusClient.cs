using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Spar.Testing.BlackBox.Linux;

namespace Spar.Testing;

/// <summary>
/// One GDBus connection to a message bus, with synchronous calls whose replies come back as managed values: a string
/// (<c>s</c>, <c>o</c>, <c>g</c>), a number or <c>bool</c>, a <see cref="List{T}"/> of values for an array, an object
/// array for a tuple, a <see cref="Dictionary{TKey, TValue}"/> for a dictionary, and a variant's content in its place.
/// GDBus's synchronous calls are safe from any thread; each call has a timeout and starts no service
/// (<c>NO_AUTO_START</c>) unless it asks to, so a name that is gone answers at once.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class DBusClient : IDisposable
{
    /// <summary>How long one call waits for its reply.</summary>
    public static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(2);

    private nint _connection;

    private DBusClient(nint connection) => _connection = connection;

    /// <summary>Connects to the bus at <paramref name="address"/>; throws <see cref="DBusException"/> when it cannot.</summary>
    public static DBusClient Connect(string address)
    {
        nint error = 0;
        var connection = Gio.g_dbus_connection_new_for_address_sync(address, Gio.ClientOfMessageBus, 0, 0, &error);
        if (connection == 0)
        {
            throw DBusException.From(error, $"connect to {address}");
        }

        return new DBusClient(connection);
    }

    /// <summary>
    /// Calls <paramref name="method"/> of <paramref name="interfaceName"/> on <paramref name="path"/> of
    /// <paramref name="name"/> with <paramref name="arguments"/> (strings, <c>int</c> and <c>uint</c>), and returns the reply
    /// tuple's values.
    /// </summary>
    public object[] Call(
        string? name, string path, string interfaceName, string method, object[]? arguments = null, bool autoStart = false, TimeSpan? limit = null)
    {
        var connection = _connection;
        ObjectDisposedException.ThrowIf(connection == 0, this);
        nint error = 0;
        var reply = Gio.g_dbus_connection_call_sync(
            connection, name, path, interfaceName, method, Tuple(arguments), 0, autoStart ? 0 : Gio.NoAutoStart,
            (int)(limit ?? CallLimit).TotalMilliseconds, 0, &error);
        if (reply == 0)
        {
            throw DBusException.From(error, $"{interfaceName}.{method} on {name} {path}");
        }

        try
        {
            return (object[])Read(reply);
        }
        finally
        {
            Gio.g_variant_unref(reply);
        }
    }

    /// <summary>A property through <c>org.freedesktop.DBus.Properties.Get</c>.</summary>
    public object Property(string name, string path, string interfaceName, string property) =>
        Call(name, path, "org.freedesktop.DBus.Properties", "Get", [interfaceName, property])[0];

    public void Dispose()
    {
        var connection = Interlocked.Exchange(ref _connection, 0);
        if (connection != 0)
        {
            nint error = 0;
            _ = Gio.g_dbus_connection_close_sync(connection, 0, &error);
            if (error != 0)
            {
                Gio.g_error_free(error);
            }

            Gio.g_object_unref(connection);
        }
    }

    /// <summary>A floating tuple of <paramref name="values"/>, or 0 for a call without arguments.</summary>
    private static nint Tuple(object[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return 0;
        }

        var children = stackalloc nint[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            children[index] = values[index] switch
            {
                string text => Gio.g_variant_new_string(text),
                int number => Gio.g_variant_new_int32(number),
                uint number => Gio.g_variant_new_uint32(number),
                var other => throw new ArgumentException($"A D-Bus argument of type {other?.GetType().Name} is not supported here.", nameof(values)),
            };
        }

        return Gio.g_variant_new_tuple(children, (nuint)values.Length);
    }

    /// <summary>The managed form of <paramref name="value"/> (not consumed).</summary>
    private static object Read(nint value)
    {
        var type = Marshal.PtrToStringUTF8(Gio.g_variant_get_type_string(value)) ?? "";
        switch (type[0])
        {
            case 's' or 'o' or 'g':
                nuint length;
                return Marshal.PtrToStringUTF8(Gio.g_variant_get_string(value, &length)) ?? "";
            case 'b':
                return Gio.g_variant_get_boolean(value) != 0;
            case 'y':
                return Gio.g_variant_get_byte(value);
            case 'n':
                return Gio.g_variant_get_int16(value);
            case 'q':
                return Gio.g_variant_get_uint16(value);
            case 'i':
                return Gio.g_variant_get_int32(value);
            case 'u':
                return Gio.g_variant_get_uint32(value);
            case 'x':
                return Gio.g_variant_get_int64(value);
            case 't':
                return Gio.g_variant_get_uint64(value);
            case 'd':
                return Gio.g_variant_get_double(value);
            case 'v':
                var inner = Gio.g_variant_get_variant(value);
                try
                {
                    return Read(inner);
                }
                finally
                {
                    Gio.g_variant_unref(inner);
                }

            case 'a' when type.Length > 1 && type[1] == '{':
                var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var entry in Children(value))
                {
                    var pair = (object[])entry;
                    dictionary[Convert.ToString(pair[0], System.Globalization.CultureInfo.InvariantCulture) ?? ""] = pair[1];
                }

                return dictionary;
            case 'a':
                return Children(value);
            case '(' or '{':
                return Children(value).ToArray();
            default:
                return type;
        }
    }

    private static List<object> Children(nint container)
    {
        var count = (int)Gio.g_variant_n_children(container);
        var children = new List<object>(count);
        for (var index = 0; index < count; index++)
        {
            var child = Gio.g_variant_get_child_value(container, (nuint)index);
            try
            {
                children.Add(Read(child));
            }
            finally
            {
                Gio.g_variant_unref(child);
            }
        }

        return children;
    }
}

/// <summary>A D-Bus call that failed: the remote error's name (<c>org.freedesktop.DBus.Error.UnknownMethod</c>…) and message.</summary>
internal sealed class DBusException(string? errorName, string message) : Exception(message)
{
    /// <summary>The D-Bus error name, or null for a local failure (a timeout, a closed connection).</summary>
    public string? ErrorName { get; } = errorName;

    /// <summary>
    /// The object went between two calls: its path is gone (<c>UnknownMethod</c> or <c>UnknownObject</c>, "Object does not
    /// exist at path"), or its connection (<c>ServiceUnknown</c>, <c>NameHasNoOwner</c>, <c>NoReply</c> from a name that
    /// left). Every read treats it as gone, never as a failure.
    /// </summary>
    public bool IsGone => ErrorName is "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownObject"
        or "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
        or "org.freedesktop.DBus.Error.Disconnected" or "org.freedesktop.DBus.Error.NoReply";

    [SupportedOSPlatform("linux")]
    public static unsafe DBusException From(nint error, string what)
    {
        if (error == 0)
        {
            return new DBusException(null, $"D-Bus: {what} failed.");
        }

        try
        {
            var remote = Gio.g_dbus_error_get_remote_error(error);
            var name = remote == 0 ? null : Marshal.PtrToStringUTF8(remote);
            Gio.g_free(remote);

            // GError: a 32-bit domain, a 32-bit code, then the message pointer.
            var message = Marshal.PtrToStringUTF8(*(nint*)((byte*)error + 8)) ?? "";
            return new DBusException(name, $"D-Bus: {what}: {message}");
        }
        finally
        {
            Gio.g_error_free(error);
        }
    }
}
