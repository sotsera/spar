using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>An accessible object on the accessibility bus: the connection that serves it and its path.</summary>
internal readonly record struct AtSpiNode(string Name, string Path)
{
    public bool IsNull => Name.Length == 0 || Path == "/org/a11y/atspi/null";

    public override string ToString() => $"{Name} {Path}";
}

/// <summary>
/// One launched app's accessibility bus, read through GDBus with no libatspi (decision "The Linux black box: AT-SPI
/// over GDBus"): its session bus gives the bus's address (<c>org.a11y.Bus.GetAddress</c>, which starts at-spi2-core's
/// launcher), the bus daemon names each connection's process, and the app's own connection serves its windows under
/// its root. Every read asks the app as it is now; a node that went between two calls answers "gone"
/// (<see cref="DBusException.IsGone"/>), which the callers treat as gone, never as a failure.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiBus : IDisposable
{
    public const uint RoleAlert = 2;
    public const uint RoleCheckBox = 7;
    public const uint RoleDialog = 16;
    public const uint RoleFrame = 23;
    public const uint RoleLabel = 29;
    public const uint RoleMenu = 33;
    public const uint RoleMenuItem = 35;
    public const uint RolePushButton = 43;
    public const uint RoleWindow = 69;
    public const uint RoleEntry = 79;
    public const uint RoleLink = 88;

    public const int StateEditable = 7;
    public const int StateEnabled = 8;
    public const int StateFocused = 12;
    public const int StateSensitive = 24;
    public const int StateShowing = 25;
    public const int StateVisible = 30;

    private const string Accessible = "org.a11y.atspi.Accessible";
    private const string Root = "/org/a11y/atspi/accessible/root";

    private readonly string _sessionAddress;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, string> _names = [];
    private DBusClient? _bus;

    public AtSpiBus(string sessionAddress) => _sessionAddress = sessionAddress;

    /// <summary>The app's own connection on the accessibility bus, found by its process; null while it has none.</summary>
    public string? ConnectionOf(int processId)
    {
        var bus = Bus();
        lock (_gate)
        {
            if (_names.TryGetValue(processId, out var known) && OwnerIs(bus, known, processId))
            {
                return known;
            }
        }

        var names = (List<object>)bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames")[0];
        foreach (var name in names.Cast<string>().Where(name => name.StartsWith(':')))
        {
            if (OwnerIs(bus, name, processId))
            {
                lock (_gate)
                {
                    _names[processId] = name;
                }

                return name;
            }
        }

        return null;
    }

    /// <summary>The app's top-level windows (its root's children), with their roles; empty while it has none.</summary>
    public List<(AtSpiNode Node, uint Role)> TopLevels(int processId)
    {
        var found = new List<(AtSpiNode, uint)>();
        if (ConnectionOf(processId) is not { } name)
        {
            return found;
        }

        List<AtSpiNode> children;
        try
        {
            children = Children(new AtSpiNode(name, Root));
        }
        catch (DBusException gone) when (gone.IsGone)
        {
            // The app is leaving the bus (its window closed, or it is exiting).
            return found;
        }

        foreach (var child in children)
        {
            if (TryRead(() => Role(child)) is { } role)
            {
                found.Add((child, role));
            }
        }

        return found;
    }

    public List<AtSpiNode> Children(AtSpiNode node) =>
        [.. ((List<object>)Call(node, Accessible, "GetChildren")[0]).Cast<object[]>().Select(Node).Where(child => !child.IsNull)];

    public uint Role(AtSpiNode node) => (uint)Call(node, Accessible, "GetRole")[0];

    public string NameOf(AtSpiNode node) => (string)Bus().Property(node.Name, node.Path, Accessible, "Name");

    /// <summary>The node's states as a bit set (<c>AtspiStateType</c>: bit <c>n</c> is state <c>n</c>).</summary>
    public ulong States(AtSpiNode node)
    {
        var words = (List<object>)Call(node, Accessible, "GetState")[0];
        return words.Count < 2 ? 0 : (uint)words[0] | ((ulong)(uint)words[1] << 32);
    }

    public Dictionary<string, string> Attributes(AtSpiNode node) =>
        ((Dictionary<string, object>)Call(node, Accessible, "GetAttributes")[0]).ToDictionary(pair => pair.Key, pair => (string)pair.Value, StringComparer.Ordinal);

    public List<string> Interfaces(AtSpiNode node) => [.. ((List<object>)Call(node, Accessible, "GetInterfaces")[0]).Cast<string>()];

    public string Text(AtSpiNode node) => (string)Call(node, "org.a11y.atspi.Text", "GetText", [0, -1])[0];

    public List<string> Actions(AtSpiNode node) =>
        [.. ((List<object>)Call(node, "org.a11y.atspi.Action", "GetActions")[0]).Cast<object[]>().Select(action => (string)action[0])];

    public bool DoAction(AtSpiNode node, int index) => (bool)Call(node, "org.a11y.atspi.Action", "DoAction", [index])[0];

    public bool GrabFocus(AtSpiNode node) => (bool)Call(node, "org.a11y.atspi.Component", "GrabFocus")[0];

    /// <summary>The node's rectangle relative to its window (<c>ATSPI_COORD_TYPE_WINDOW</c>): GTK 4 on X11 knows no window position.</summary>
    public ScreenRect WindowExtents(AtSpiNode node)
    {
        var box = (object[])Call(node, "org.a11y.atspi.Component", "GetExtents", [1u])[0];
        int At(int index) => (int)box[index];
        return new ScreenRect(At(0), At(1), At(0) + At(2), At(1) + At(3));
    }

    /// <summary>The value <paramref name="read"/> gives, or null when the node went meanwhile.</summary>
    public static T? TryRead<T>(Func<T> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (DBusException gone) when (gone.IsGone)
        {
            return null;
        }
    }

    public static bool Has(ulong states, int state) => (states & (1UL << state)) != 0;

    public void Dispose() => _bus?.Dispose();

    private object[] Call(AtSpiNode node, string interfaceName, string method, object[]? arguments = null) =>
        Bus().Call(node.Name, node.Path, interfaceName, method, arguments);

    private static AtSpiNode Node(object[] reference) => new((string)reference[0], (string)reference[1]);

    private static bool OwnerIs(DBusClient bus, string name, int processId)
    {
        try
        {
            var owner = (uint)bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetConnectionUnixProcessID", [name])[0];
            return owner == (uint)processId;
        }
        catch (DBusException)
        {
            return false;
        }
    }

    /// <summary>The accessibility bus, asked of the app's session bus at the first read (which starts its launcher).</summary>
    private DBusClient Bus()
    {
        lock (_gate)
        {
            if (_bus is { } bus)
            {
                return bus;
            }

            using var session = DBusClient.Connect(_sessionAddress);
            var address = (string)session.Call("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress", autoStart: true)[0];
            return _bus = DBusClient.Connect(address);
        }
    }
}

/// <summary>AT-SPI's text, as the black box compares it.</summary>
internal static class AtSpiText
{
    /// <summary>A text element's own text: the embedded objects' characters (U+FFFC) out, white space collapsed, trimmed.</summary>
    public static string Clean(string text) =>
        string.Join(' ', text.Replace('￼', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
