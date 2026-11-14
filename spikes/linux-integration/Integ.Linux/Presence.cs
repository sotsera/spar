using System.Globalization;

namespace Integ.Linux;

/// <summary>
/// Question 9: which desktop services are present, without triggering anything visible. Calls use
/// G_DBUS_CALL_FLAGS_NO_AUTO_START, so nothing is activated; no notification is sent.
/// </summary>
internal static class Presence
{
    private const int NoAutoStart = 1;

    public static int Run()
    {
        var bus = Gio.g_bus_get_sync(Gio.BusTypeSession, 0, out var error);
        if (bus == 0)
        {
            Log.Mark("presence: no session bus: " + GLib.TakeError(error));
            return 1;
        }

        var names = Call(bus, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", 0, 0);
        var activatable = Call(bus, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListActivatableNames", 0, 0);
        foreach (var name in new[] { "org.freedesktop.Notifications", "org.freedesktop.portal.Desktop", "org.kde.StatusNotifierWatcher", "com.canonical.Unity", "org.gnome.Shell" })
        {
            Log.Mark($"presence name {name}: owned={names.Contains("'" + name + "'", StringComparison.Ordinal)} activatable={activatable.Contains("'" + name + "'", StringComparison.Ordinal)}");
        }

        if (names.Contains("'org.freedesktop.Notifications'", StringComparison.Ordinal))
        {
            Log.Mark("presence Notifications.GetCapabilities " + Call(bus, "org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", "GetCapabilities", 0, NoAutoStart));
            Log.Mark("presence Notifications.GetServerInformation " + Call(bus, "org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", "GetServerInformation", 0, NoAutoStart));
        }

        if (names.Contains("'org.freedesktop.portal.Desktop'", StringComparison.Ordinal))
        {
            var xml = Call(bus, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop", "org.freedesktop.DBus.Introspectable", "Introspect", 0, NoAutoStart);
            // g_variant_print quotes the XML with single quotes and leaves its double quotes as they are.
            var interfaces = xml.Split("interface name=\"", StringSplitOptions.None).Skip(1).Select(part => part[..part.IndexOf('"', StringComparison.Ordinal)])
                .Where(name => name.StartsWith("org.freedesktop.portal.", StringComparison.Ordinal)).ToList();

            Log.Mark("presence portal interfaces: " + string.Join(' ', interfaces));
            foreach (var iface in new[] { "Notification", "GlobalShortcuts", "FileChooser", "Settings", "Background", "Inhibit", "OpenURI", "Print", "Account", "Secret" })
            {
                var version = Call(bus, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop", "org.freedesktop.DBus.Properties", "Get",
                    GLib.Parse("(ss)", $"('org.freedesktop.portal.{iface}', 'version')"), NoAutoStart);
                Log.Mark($"presence portal {iface} version: {version}");
            }
        }
        else
        {
            Log.Mark("presence portal: not running (not activated by this probe)");
        }

        GObject.g_object_unref(bus);
        return 0;
    }

    /// <summary>The Unity launcher API: progress and count badge, a broadcast signal with no reply.</summary>
    public static void EmitLauncherEntry(string argument)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var progress = parts.Length > 0 ? double.Parse(parts[0], CultureInfo.InvariantCulture) : 0.5;
        var count = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : 3;
        var bus = Gio.g_bus_get_sync(Gio.BusTypeSession, 0, out var error);
        if (bus == 0)
        {
            Log.Mark("launcher: no session bus: " + GLib.TakeError(error));
            return;
        }

        var parameters = GLib.Parse("(sa{sv})", string.Create(CultureInfo.InvariantCulture,
            $"('application://spar-integration.desktop', {{'progress': <{progress:F2}>, 'progress-visible': <true>, 'count': <int64 {count}>, 'count-visible': <true>, 'urgent': <false>}})"));
        var ok = Gio.g_dbus_connection_emit_signal(bus, null, "/com/canonical/unity/launcherentry/spar_integration", "com.canonical.Unity.LauncherEntry", "Update", parameters, out error);
        GLib.g_variant_unref(parameters);
        Gio.g_dbus_connection_flush_sync(bus, 0, out _);
        Log.Mark(ok != 0 ? $"launcher Update emitted progress={progress} count={count}" : "launcher emit failed: " + GLib.TakeError(error));
        GObject.g_object_unref(bus);
    }

    private static string Call(nint bus, string name, string path, string iface, string method, nint parameters, int flags)
    {
        var reply = Gio.g_dbus_connection_call_sync(bus, name, path, iface, method, parameters, null, flags, 2000, 0, out var error);
        if (reply == 0)
        {
            return "(error: " + GLib.TakeError(error) + ")";
        }

        var text = GLib.Print(reply);
        GLib.g_variant_unref(reply);
        return text;
    }
}
