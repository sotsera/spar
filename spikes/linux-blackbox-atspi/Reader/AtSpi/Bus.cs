using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.AtSpi;

/// <summary>An accessible object on the accessibility bus: its owner's bus name and its object path.</summary>
internal readonly record struct Ref(string Name, string Path)
{
    public override string ToString() => $"{Name}{Path}";
}

/// <summary>One signal received on the accessibility bus, with when it arrived (<see cref="Stopwatch"/> ticks).</summary>
internal sealed record Signal(long At, string Sender, string Path, string Interface, string Member, string Detail);

/// <summary>
/// AT-SPI over the accessibility bus through GIO's GDBus directly, no libatspi: the bus's address from
/// <c>AT_SPI_BUS_ADDRESS</c> or <c>org.a11y.Bus.GetAddress</c> on the session bus, then plain method calls on the
/// AT-SPI interfaces. Replies are converted to managed values at once (strings, numbers, arrays and tuples as
/// <c>object?[]</c>) and released. GDBus's synchronous calls may come from any thread.
/// </summary>
internal sealed unsafe class Bus : IDisposable
{
    public const string Accessible = "org.a11y.atspi.Accessible";
    public const string RootPath = "/org/a11y/atspi/accessible/root";
    public const string Registry = "org.a11y.atspi.Registry";

    private static readonly ConcurrentQueue<Signal> Received = new();
    private static readonly SemaphoreSlim Arrived = new(0);

    private nint _connection;
    private nint _loop;
    private Thread? _signals;

    private Bus(nint connection, string address, string source)
    {
        _connection = connection;
        Address = address;
        AddressSource = source;
    }

    public string Address { get; }

    public string AddressSource { get; }

    public string UniqueName => Marshal.PtrToStringUTF8(GLib.g_dbus_connection_get_unique_name(_connection)) ?? "";

    /// <summary>The accessibility bus's address: the environment's, else the session bus's <c>org.a11y.Bus</c> (activated on demand).</summary>
    public static (string? Address, string Source, string? Error) FindAddress()
    {
        if (Environment.GetEnvironmentVariable("AT_SPI_BUS_ADDRESS") is { Length: > 0 } fromEnvironment)
        {
            return (fromEnvironment, "AT_SPI_BUS_ADDRESS", null);
        }

        nint error = 0;
        var session = GLib.g_bus_get_sync(GLib.BusSession, 0, &error);
        if (session == 0)
        {
            return (null, "session bus", GLib.TakeError(error));
        }

        try
        {
            var reply = Call(session, "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress", null, 5_000, out var failure);
            return reply is null ? (null, "org.a11y.Bus", failure) : ((string)reply[0]!, "org.a11y.Bus", null);
        }
        finally
        {
            GLib.g_object_unref(session);
        }
    }

    public static Bus Connect()
    {
        var (address, source, error) = FindAddress();
        if (address is null)
        {
            throw new InvalidOperationException($"No accessibility bus ({source}): {error}");
        }

        nint gerror = 0;
        var connection = GLib.g_dbus_connection_new_for_address_sync(address, GLib.ClientOfMessageBus, 0, 0, &gerror);
        return connection != 0
            ? new Bus(connection, address, source)
            : throw new InvalidOperationException($"The accessibility bus {address} refused the connection: {GLib.TakeError(gerror)}");
    }

    /// <summary>A method call; the reply's values, or null with <paramref name="error"/>.</summary>
    public object?[]? Call(string? name, string path, string iface, string method, object?[]? arguments, out string? error, int timeout = 2_000) =>
        Call(_connection, name, path, iface, method, arguments, timeout, out error);

    public object?[] CallOrThrow(string? name, string path, string iface, string method, params object?[] arguments) =>
        Call(_connection, name, path, iface, method, arguments.Length == 0 ? null : arguments, 2_000, out var error)
            ?? throw new InvalidOperationException($"{iface}.{method} on {name}{path}: {error}");

    // --- The bus daemon ---------------------------------------------------------------------------------------------

    public string[] Names() => ((object?[])CallOrThrow("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames")[0]!)
        .Cast<string>().ToArray();

    public int? ProcessOf(string name) =>
        Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetConnectionUnixProcessID", [name], out _) is { } reply
            ? (int)(uint)reply[0]! : null;

    /// <summary>The bus names (unique ones) that belong to <paramref name="processId"/>, asked of the bus daemon only.</summary>
    public List<string> NamesOf(int processId)
    {
        var mine = UniqueName;
        return Names().Where(n => n.StartsWith(':') && n != mine && ProcessOf(n) == processId).ToList();
    }

    /// <summary>The applications the registry lists (its root's children).</summary>
    public List<Ref> RegistryApplications(out string? error) =>
        Call(Registry, RootPath, Accessible, "GetChildren", null, out error) is { } reply ? Refs(reply[0]) : [];

    // --- One accessible ---------------------------------------------------------------------------------------------

    public List<Ref> Children(Ref node) => Refs(CallOrThrow(node.Name, node.Path, Accessible, "GetChildren")[0]);

    public string Name(Ref node) => (string)Property(node, Accessible, "Name")!;

    public int ChildCount(Ref node) => (int)Property(node, Accessible, "ChildCount")!;

    public uint Role(Ref node) => (uint)CallOrThrow(node.Name, node.Path, Accessible, "GetRole")[0]!;

    public string RoleName(Ref node) => (string)CallOrThrow(node.Name, node.Path, Accessible, "GetRoleName")[0]!;

    public string[] Interfaces(Ref node) => ((object?[])CallOrThrow(node.Name, node.Path, Accessible, "GetInterfaces")[0]!).Cast<string>().ToArray();

    /// <summary>The state set as a 64-bit mask (AtspiStateType bits).</summary>
    public ulong States(Ref node)
    {
        var words = (object?[])CallOrThrow(node.Name, node.Path, Accessible, "GetState")[0]!;
        return (uint)words[0]! | ((ulong)(uint)words[1]! << 32);
    }

    public string Text(Ref node) => (string)CallOrThrow(node.Name, node.Path, "org.a11y.atspi.Text", "GetText", 0, -1)[0]!;

    /// <summary>The actions' names (<c>org.a11y.atspi.Action.GetActions</c>: name, description, key binding).</summary>
    public string[] Actions(Ref node) => ((object?[])CallOrThrow(node.Name, node.Path, "org.a11y.atspi.Action", "GetActions")[0]!)
        .Select(a => (string)((object?[])a!)[0]!).ToArray();

    public bool DoAction(Ref node, int index) => (bool)CallOrThrow(node.Name, node.Path, "org.a11y.atspi.Action", "DoAction", index)[0]!;

    /// <summary>The screen rectangle (<c>ATSPI_COORD_TYPE_SCREEN</c>).</summary>
    public (int X, int Y, int Width, int Height) Extents(Ref node)
    {
        var box = (object?[])CallOrThrow(node.Name, node.Path, "org.a11y.atspi.Component", "GetExtents", 0u)[0]!;
        return ((int)box[0]!, (int)box[1]!, (int)box[2]!, (int)box[3]!);
    }

    public object? Property(Ref node, string iface, string property) =>
        CallOrThrow(node.Name, node.Path, "org.freedesktop.DBus.Properties", "Get", iface, property)[0];

    /// <summary>
    /// The whole tree of an application in one call, if its toolkit serves <c>org.a11y.atspi.Cache.GetItems</c>: per item
    /// its ref, application, parent, index, child count, interfaces, name, role, description and states.
    /// </summary>
    public object?[]? CacheItems(string name, out string? error) =>
        Call(name, "/org/a11y/atspi/cache", "org.a11y.atspi.Cache", "GetItems", null, out error) is { } reply ? (object?[])reply[0]! : null;

    // --- Signals ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Subscribes to every AT-SPI event interface on a thread of its own with its own main context and loop (GDBus
    /// delivers a subscription's signals on the context that was the thread's default when it subscribed).
    /// </summary>
    public void Listen()
    {
        using var ready = new ManualResetEventSlim();
        _signals = new Thread(() =>
        {
            var context = GLib.g_main_context_new();
            GLib.g_main_context_push_thread_default(context);
            foreach (var iface in new[] { "org.a11y.atspi.Event.Window", "org.a11y.atspi.Event.Object", "org.a11y.atspi.Event.Focus" })
            {
                GLib.g_dbus_connection_signal_subscribe(_connection, null, iface, null, null, null, 0, &OnSignal, 0, 0);
            }

            _loop = GLib.g_main_loop_new(context, 0);
            ready.Set();
            GLib.g_main_loop_run(_loop);
        }) { IsBackground = true, Name = "AT-SPI signals" };
        _signals.Start();
        ready.Wait();
    }

    public static void ClearSignals()
    {
        while (Received.TryDequeue(out _))
        {
        }
    }

    /// <summary>The first signal <paramref name="match"/> accepts, waiting until <paramref name="deadline"/>; null when none came.</summary>
    public static Signal? WaitSignal(Func<Signal, bool> match, Stopwatch clock, long deadlineMs, List<Signal>? seen = null)
    {
        while (true)
        {
            while (Received.TryDequeue(out var signal))
            {
                seen?.Add(signal);
                if (match(signal))
                {
                    return signal;
                }
            }

            var left = deadlineMs - clock.ElapsedMilliseconds;
            if (left <= 0 || !Arrived.Wait((int)left))
            {
                if (Received.IsEmpty)
                {
                    return null;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_loop != 0)
        {
            GLib.g_main_loop_quit(_loop);
        }

        if (_connection != 0)
        {
            GLib.g_object_unref(_connection);
            _connection = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSignal(nint connection, nint sender, nint path, nint iface, nint member, nint parameters, nint data)
    {
        try
        {
            var values = Convert(parameters) as object?[];
            var detail = values is { Length: > 0 } && values[0] is string kind ? kind : "";
            if (values is { Length: > 3 })
            {
                detail += $" {values[1]} {values[2]} {Describe(values[3])}";
            }

            Received.Enqueue(new Signal(
                Stopwatch.GetTimestamp(),
                Marshal.PtrToStringUTF8(sender) ?? "",
                Marshal.PtrToStringUTF8(path) ?? "",
                Marshal.PtrToStringUTF8(iface) ?? "",
                Marshal.PtrToStringUTF8(member) ?? "",
                detail));
            Arrived.Release();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"signal handler: {exception.Message}");
        }
    }

    // --- Values -----------------------------------------------------------------------------------------------------

    public static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        object?[] items => "(" + string.Join(", ", items.Select(Describe)) + ")",
        _ => value.ToString() ?? "",
    };

    private static List<Ref> Refs(object? array) =>
        ((object?[])array!).Select(item => (object?[])item!).Select(pair => new Ref((string)pair[0]!, (string)pair[1]!)).ToList();

    private static object?[]? Call(nint connection, string? name, string path, string iface, string method, object?[]? arguments, int timeout, out string? error)
    {
        nint gerror = 0;
        var parameters = arguments is null ? 0 : Build(arguments);
        var reply = GLib.g_dbus_connection_call_sync(connection, name, path, iface, method, parameters, 0, 0, timeout, 0, &gerror);
        if (reply == 0)
        {
            error = GLib.TakeError(gerror) ?? "no reply";
            return null;
        }

        try
        {
            error = null;
            return (object?[])Convert(reply)!;
        }
        finally
        {
            GLib.g_variant_unref(reply);
        }
    }

    /// <summary>A floating tuple of the arguments (string, int, uint, string array).</summary>
    private static nint Build(object?[] arguments)
    {
        var children = stackalloc nint[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            children[i] = arguments[i] switch
            {
                string text => GLib.g_variant_new_string(text),
                int number => GLib.g_variant_new_int32(number),
                uint number => GLib.g_variant_new_uint32(number),
                string[] texts => Strings(texts),
                _ => throw new ArgumentException($"No GVariant for {arguments[i]?.GetType().Name}"),
            };
        }

        return GLib.g_variant_new_tuple(children, (nuint)arguments.Length);
    }

    private static nint Strings(string[] texts)
    {
        var pointers = new nint[texts.Length];
        try
        {
            for (var i = 0; i < texts.Length; i++)
            {
                pointers[i] = Marshal.StringToCoTaskMemUTF8(texts[i]);
            }

            fixed (nint* start = pointers)
            {
                return GLib.g_variant_new_strv(start, texts.Length);
            }
        }
        finally
        {
            foreach (var pointer in pointers)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    /// <summary>A GVariant (borrowed) as managed values.</summary>
    private static object? Convert(nint value)
    {
        var type = Marshal.PtrToStringUTF8(GLib.g_variant_get_type_string(value)) ?? "";
        switch (type[0])
        {
            case 's' or 'o' or 'g':
                nuint length;
                return Marshal.PtrToStringUTF8(GLib.g_variant_get_string(value, &length));
            case 'u':
                return GLib.g_variant_get_uint32(value);
            case 'i':
                return GLib.g_variant_get_int32(value);
            case 'x':
                return GLib.g_variant_get_int64(value);
            case 't':
                return GLib.g_variant_get_uint64(value);
            case 'b':
                return GLib.g_variant_get_boolean(value) != 0;
            case 'y':
                return GLib.g_variant_get_byte(value);
            case 'd':
                return GLib.g_variant_get_double(value);
            case 'v':
                var inner = GLib.g_variant_get_variant(value);
                try
                {
                    return Convert(inner);
                }
                finally
                {
                    GLib.g_variant_unref(inner);
                }

            case '(' or 'a' or '{':
                var count = (int)GLib.g_variant_n_children(value);
                var items = new object?[count];
                for (var i = 0; i < count; i++)
                {
                    var child = GLib.g_variant_get_child_value(value, (nuint)i);
                    try
                    {
                        items[i] = Convert(child);
                    }
                    finally
                    {
                        GLib.g_variant_unref(child);
                    }
                }

                return items;
            default:
                return $"<{type}>";
        }
    }
}
