using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Integ.Linux;

/// <summary>
/// A tray icon through the freedesktop StatusNotifierItem protocol and the com.canonical.dbusmenu menu, over GDBus.
/// The item owns a well-known name, exports /StatusNotifierItem and /MenuBar, and watches
/// org.kde.StatusNotifierWatcher: whenever a watcher appears (first start or restart) it registers again; while none
/// is present the tray is reported unavailable.
/// </summary>
internal sealed unsafe class Tray
{
    private const string ItemPath = "/StatusNotifierItem";
    private const string MenuPath = "/MenuBar";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string MenuInterface = "com.canonical.dbusmenu";
    private const string Watcher = "org.kde.StatusNotifierWatcher";

    // Introspection data for the two D-Bus interfaces, as defined by the StatusNotifierItem specification
    // (freedesktop.org) and the dbusmenu interface description.
    private const string Introspection = """
        <node>
          <interface name="org.kde.StatusNotifierItem">
            <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
            <signal name="NewTitle"/>
            <signal name="NewIcon"/>
            <signal name="NewAttentionIcon"/>
            <signal name="NewOverlayIcon"/>
            <signal name="NewToolTip"/>
            <signal name="NewStatus"><arg name="status" type="s"/></signal>
            <property name="Category" type="s" access="read"/>
            <property name="Id" type="s" access="read"/>
            <property name="Title" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="WindowId" type="i" access="read"/>
            <property name="IconThemePath" type="s" access="read"/>
            <property name="IconName" type="s" access="read"/>
            <property name="IconPixmap" type="a(iiay)" access="read"/>
            <property name="OverlayIconName" type="s" access="read"/>
            <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionIconName" type="s" access="read"/>
            <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionMovieName" type="s" access="read"/>
            <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
            <property name="ItemIsMenu" type="b" access="read"/>
            <property name="Menu" type="o" access="read"/>
          </interface>
          <interface name="com.canonical.dbusmenu">
            <method name="GetLayout">
              <arg name="parentId" type="i" direction="in"/><arg name="recursionDepth" type="i" direction="in"/>
              <arg name="propertyNames" type="as" direction="in"/>
              <arg name="revision" type="u" direction="out"/><arg name="layout" type="(ia{sv}av)" direction="out"/>
            </method>
            <method name="GetGroupProperties">
              <arg name="ids" type="ai" direction="in"/><arg name="propertyNames" type="as" direction="in"/>
              <arg name="properties" type="a(ia{sv})" direction="out"/>
            </method>
            <method name="GetProperty">
              <arg name="id" type="i" direction="in"/><arg name="name" type="s" direction="in"/><arg name="value" type="v" direction="out"/>
            </method>
            <method name="Event">
              <arg name="id" type="i" direction="in"/><arg name="eventId" type="s" direction="in"/>
              <arg name="data" type="v" direction="in"/><arg name="timestamp" type="u" direction="in"/>
            </method>
            <method name="EventGroup">
              <arg name="events" type="a(isvu)" direction="in"/><arg name="idErrors" type="ai" direction="out"/>
            </method>
            <method name="AboutToShow">
              <arg name="id" type="i" direction="in"/><arg name="needUpdate" type="b" direction="out"/>
            </method>
            <method name="AboutToShowGroup">
              <arg name="ids" type="ai" direction="in"/><arg name="updatesNeeded" type="ai" direction="out"/><arg name="idErrors" type="ai" direction="out"/>
            </method>
            <signal name="ItemsPropertiesUpdated"><arg type="a(ia{sv})"/><arg type="a(ias)"/></signal>
            <signal name="LayoutUpdated"><arg type="u"/><arg type="i"/></signal>
            <signal name="ItemActivationRequested"><arg type="i"/><arg type="u"/></signal>
            <property name="Version" type="u" access="read"/>
            <property name="TextDirection" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="IconThemePath" type="as" access="read"/>
          </interface>
        </node>
        """;

    private static Tray? s_current;

    private readonly string _id;
    private readonly Action<string> _onAction;
    private readonly string _busName;
    private GDBusInterfaceVTable* _vtable;
    private nint _bus;
    private nint _nodeInfo;
    private uint _itemRegistration;
    private uint _menuRegistration;
    private uint _watch;
    private string _iconName = "spar-tray";
    private string _status = "Active";
    private string _statusText = "Spar: idle";
    private uint _layoutRevision = 1;
    private double _watcherAppearedAt;

    public Tray(string id, Action<string> onAction)
    {
        _id = id;
        _onAction = onAction;
        _busName = string.Create(CultureInfo.InvariantCulture, $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1");
    }

    public void Start()
    {
        s_current = this;
        var started = Log.Monotonic;
        _bus = Gio.g_bus_get_sync(Gio.BusTypeSession, 0, out var error);
        if (_bus == 0)
        {
            Log.Mark("tray unavailable: no session bus: " + GLib.TakeError(error));
            return;
        }

        _nodeInfo = Gio.g_dbus_node_info_new_for_xml(Introspection, out error);
        if (_nodeInfo == 0)
        {
            Log.Mark("tray introspection failed: " + GLib.TakeError(error));
            return;
        }

        _vtable = (GDBusInterfaceVTable*)NativeMemory.AllocZeroed((nuint)sizeof(GDBusInterfaceVTable));
        _vtable->MethodCall = &OnMethodCall;
        _vtable->GetProperty = &OnGetProperty;
        _itemRegistration = Gio.g_dbus_connection_register_object(_bus, ItemPath, Gio.g_dbus_node_info_lookup_interface(_nodeInfo, ItemInterface), _vtable, 0, 0, out error);
        if (_itemRegistration == 0)
        {
            Log.Mark("tray item export failed: " + GLib.TakeError(error));
            return;
        }

        _menuRegistration = Gio.g_dbus_connection_register_object(_bus, MenuPath, Gio.g_dbus_node_info_lookup_interface(_nodeInfo, MenuInterface), _vtable, 0, 0, out error);
        // DBUS_NAME_FLAG_DO_NOT_QUEUE (4): the name is ours or the call fails.
        var reply = Gio.g_dbus_connection_call_sync(_bus, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName",
            GLib.Parse("(su)", $"({GLib.Quote(_busName)}, uint32 4)"), "(u)", 0, 2000, 0, out error);
        var owned = reply == 0 ? $"failed: {GLib.TakeError(error)}" : GLib.Print(reply);
        if (reply != 0)
        {
            GLib.g_variant_unref(reply);
        }

        _watch = Gio.g_bus_watch_name_on_connection(_bus, Watcher, 0, &OnWatcherAppeared, &OnWatcherVanished, 0, 0);
        Log.Mark(string.Create(CultureInfo.InvariantCulture,
            $"tray exported name={_busName} unique={Marshal.PtrToStringUTF8(Gio.g_dbus_connection_get_unique_name(_bus))} request-name={owned} in {Log.Monotonic - started:F1} ms; watching {Watcher}"));
    }

    public void Stop()
    {
        if (_bus == 0)
        {
            return;
        }

        if (_watch != 0) Gio.g_bus_unwatch_name(_watch);
        if (_itemRegistration != 0) Gio.g_dbus_connection_unregister_object(_bus, _itemRegistration);
        if (_menuRegistration != 0) Gio.g_dbus_connection_unregister_object(_bus, _menuRegistration);
        Gio.g_dbus_connection_flush_sync(_bus, 0, out _);
        GObject.g_object_unref(_bus);
        _bus = 0;
        NativeMemory.Free(_vtable);
        Log.Mark("tray stopped");
    }

    public void SetStatusText(string text)
    {
        _statusText = text;
        Emit(ItemPath, ItemInterface, "NewToolTip");
        Emit(ItemPath, ItemInterface, "NewTitle");
        Log.Mark("tray status text set: " + text);
    }

    public void SetIcon(string name)
    {
        _iconName = name;
        Emit(ItemPath, ItemInterface, "NewIcon");
        Log.Mark("tray icon set: " + name);
    }

    public void SetStatus(string status)
    {
        _status = status;
        Emit(ItemPath, ItemInterface, "NewStatus", GLib.Parse("(s)", $"({GLib.Quote(status)},)"));
        Log.Mark("tray status set: " + status);
    }

    private void Emit(string path, string iface, string signal, nint parameters = 0)
    {
        if (_bus == 0)
        {
            return;
        }

        if (Gio.g_dbus_connection_emit_signal(_bus, null, path, iface, signal, parameters, out var error) == 0)
        {
            Log.Mark($"tray emit {signal} failed: {GLib.TakeError(error)}");
        }

        if (parameters != 0)
        {
            GLib.g_variant_unref(parameters);
        }
    }

    private void Register()
    {
        _watcherAppearedAt = Log.Monotonic;
        Gio.g_dbus_connection_call(_bus, Watcher, "/StatusNotifierWatcher", Watcher, "RegisterStatusNotifierItem",
            GLib.Parse("(s)", $"({GLib.Quote(_busName)},)"), null, 0, 2000, 0, &OnRegistered, 0);
    }

    // ---- Property values ----

    /// <summary>A 16×16 ARGB32 icon (network byte order): a filled circle, coloured by status, so a panel capture can find it.</summary>
    private string Pixmap()
    {
        var (r, g, b) = _status == "NeedsAttention" ? (230, 60, 40) : _iconName == "spar-tray-busy" ? (240, 180, 0) : (30, 144, 255);
        var builder = new StringBuilder("[(16, 16, [byte ");
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var inside = (x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5) <= 56;
                if (inside)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"255, {r}, {g}, {b}, ");
                }
                else
                {
                    builder.Append("0, 0, 0, 0, ");
                }
            }
        }

        builder.Length -= 2;
        return builder.Append("])]").ToString();
    }

    private nint Property(string name) => name switch
    {
        "Category" => GLib.Parse("s", "'ApplicationStatus'"),
        "Id" => GLib.Parse("s", GLib.Quote(_id)),
        "Title" => GLib.Parse("s", GLib.Quote("Spar integration (" + _statusText + ")")),
        "Status" => GLib.Parse("s", GLib.Quote(_status)),
        "WindowId" => GLib.Parse("i", "0"),
        "IconThemePath" => GLib.Parse("s", "''"),
        // A name no icon theme has, so hosts fall back to the pixmap (which is what a packaged app would ship anyway).
        "IconName" => GLib.Parse("s", GLib.Quote(_iconName)),
        "IconPixmap" => GLib.Parse("a(iiay)", Pixmap()),
        "OverlayIconName" or "AttentionIconName" or "AttentionMovieName" => GLib.Parse("s", "''"),
        "OverlayIconPixmap" or "AttentionIconPixmap" => GLib.Parse("a(iiay)", "[]"),
        "ToolTip" => GLib.Parse("(sa(iiay)ss)", $"('', [], 'Spar integration', {GLib.Quote(_statusText)})"),
        "ItemIsMenu" => GLib.Parse("b", "false"),
        "Menu" => GLib.Parse("o", "'" + MenuPath + "'"),
        "Version" => GLib.Parse("u", "3"),
        "TextDirection" => GLib.Parse("s", "'ltr'"),
        _ => 0,
    };

    private static string MenuItem(int id) => id switch
    {
        1 => "(1, {'label': <'Restore'>, 'enabled': <true>, 'visible': <true>}, @av [])",
        2 => "(2, {'type': <'separator'>}, @av [])",
        3 => "(3, {'label': <'Quit'>, 'enabled': <true>, 'visible': <true>}, @av [])",
        _ => "",
    };

    private string Layout() =>
        $"(uint32 {_layoutRevision}, (0, {{'children-display': <'submenu'>}}, [<{MenuItem(1)}>, <{MenuItem(2)}>, <{MenuItem(3)}>]))";

    private nint MenuStatus(string name) => name switch
    {
        "Status" => GLib.Parse("s", "'normal'"),
        "IconThemePath" => GLib.Parse("as", "@as []"),
        _ => Property(name),
    };

    // ---- Method calls ----

    private void Call(string sender, string path, string iface, string method, nint parameters, nint invocation)
    {
        var arguments = GLib.Print(parameters);
        Log.Mark($"tray call {iface}.{method}{arguments} from {sender}");
        nint result = 0;
        switch (method)
        {
            case "Activate":
                Post("activate");
                break;
            case "SecondaryActivate":
            case "Scroll":
                break;
            case "ContextMenu":
                // A host that renders the dbusmenu itself never calls this; nothing to show from the app side.
                break;
            case "GetLayout":
                result = GLib.Parse("(u(ia{sv}av))", Layout());
                break;
            case "GetGroupProperties":
                result = GLib.Parse("(a(ia{sv}))", $"([{string.Join(", ", new[] { 1, 2, 3 }.Select(id => MenuItem(id)[..MenuItem(id).LastIndexOf(", @av", StringComparison.Ordinal)] + ")"))}],)");
                break;
            case "GetProperty":
                result = GLib.Parse("(v)", "(<''>,)");
                break;
            case "AboutToShow":
                result = GLib.Parse("(b)", "(false,)");
                break;
            case "AboutToShowGroup":
                result = GLib.Parse("(aiai)", "(@ai [], @ai [])");
                break;
            case "Event":
                MenuEvent(GLib.ChildInt32(parameters, 0), GLib.ChildString(parameters, 1));
                break;
            case "EventGroup":
                var events = GLib.g_variant_get_child_value(parameters, 0);
                for (nuint i = 0; i < GLib.g_variant_n_children(events); i++)
                {
                    var item = GLib.g_variant_get_child_value(events, i);
                    MenuEvent(GLib.ChildInt32(item, 0), GLib.ChildString(item, 1));
                    GLib.g_variant_unref(item);
                }

                GLib.g_variant_unref(events);
                result = GLib.Parse("(ai)", "(@ai [],)");
                break;
            default:
                Gio.g_dbus_method_invocation_return_dbus_error(invocation, "org.freedesktop.DBus.Error.UnknownMethod", method);
                return;
        }

        // return_value adds its own reference to a non-floating value, so ours is dropped afterwards.
        Gio.g_dbus_method_invocation_return_value(invocation, result);
        if (result != 0)
        {
            GLib.g_variant_unref(result);
        }
    }

    private void MenuEvent(int id, string eventId)
    {
        Log.Mark($"tray menu event id={id} {eventId}");
        if (eventId != "clicked")
        {
            return;
        }

        Post(id switch { 1 => "restore", 3 => "quit", _ => "none" });
    }

    /// <summary>Runs the action after the D-Bus reply has been queued, outside the GDBus callback.</summary>
    private void Post(string action) => SynchronizationContext.Current!.Post(static state => { var (tray, a) = ((Tray, string))state!; tray._onAction(a); }, (this, action));

    [UnmanagedCallersOnly]
    private static void OnMethodCall(nint connection, nint sender, nint path, nint iface, nint method, nint parameters, nint invocation, nint data)
    {
        try
        {
            s_current!.Call(Marshal.PtrToStringUTF8(sender) ?? "", Marshal.PtrToStringUTF8(path) ?? "", Marshal.PtrToStringUTF8(iface) ?? "",
                Marshal.PtrToStringUTF8(method) ?? "", parameters, invocation);
        }
        catch (Exception exception)
        {
            Log.Mark("tray call exception " + exception);
            Gio.g_dbus_method_invocation_return_dbus_error(invocation, "org.freedesktop.DBus.Error.Failed", exception.Message);
        }
    }

    [UnmanagedCallersOnly]
    private static nint OnGetProperty(nint connection, nint sender, nint path, nint iface, nint property, nint* error, nint data)
    {
        try
        {
            var name = Marshal.PtrToStringUTF8(property) ?? "";
            var value = Marshal.PtrToStringUTF8(iface) == MenuInterface ? s_current!.MenuStatus(name) : s_current!.Property(name);
            return value;
        }
        catch (Exception exception)
        {
            Log.Mark("tray property exception " + exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWatcherAppeared(nint connection, nint name, nint owner, nint data)
    {
        Log.Mark($"tray watcher appeared owner={Marshal.PtrToStringUTF8(owner)}; registering");
        try
        {
            s_current!.Register();
        }
        catch (Exception exception)
        {
            Log.Mark("tray register exception " + exception);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWatcherVanished(nint connection, nint name, nint data) =>
        Log.Mark("tray unavailable: no StatusNotifierWatcher on the session bus");

    [UnmanagedCallersOnly]
    private static void OnRegistered(nint source, nint result, nint data)
    {
        var tray = s_current!;
        var reply = Gio.g_dbus_connection_call_finish(source, result, out var error);
        var took = Log.Monotonic - tray._watcherAppearedAt;
        if (reply == 0)
        {
            Log.Mark(string.Create(CultureInfo.InvariantCulture, $"tray register failed after {took:F1} ms: {GLib.TakeError(error)}"));
            return;
        }

        GLib.g_variant_unref(reply);
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"tray registered after {took:F1} ms"));
    }
}
