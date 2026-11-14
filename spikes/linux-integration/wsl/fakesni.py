# A minimal StatusNotifierWatcher plus tray host, for checking the host's tray item where no real tray exists (WSLg).
# Usage (inside WSL, in a private D-Bus session): python3 -I wsl/fakesni.py <control fifo> [--no-host]
# It owns org.kde.StatusNotifierWatcher, accepts RegisterStatusNotifierItem, and as the host reads the item's
# properties, follows its signals and calls it like a panel would. Commands (one per line on the FIFO):
#   props                 GetAll on the item (logged without the pixmap bytes, with its size and a checksum)
#   activate | secondary  Activate(x, y) / SecondaryActivate(x, y): a left / middle click
#   contextmenu           ContextMenu(x, y): what a host that does not render dbusmenu itself calls
#   menu                  AboutToShow(0) then GetLayout(0, -1, []) on the item's Menu object
#   click LABEL           Event(id, "clicked", <0>, time) on the menu item with that label
#   quit                  exit (the watcher's name goes away with the process)
# Lines on stdout: "F <monotonic ms> <event>", the same clock as the host's marks.
import hashlib, os, sys, threading, time

import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

WATCHER_XML = """
<node><interface name="org.kde.StatusNotifierWatcher">
  <method name="RegisterStatusNotifierItem"><arg name="service" type="s" direction="in"/></method>
  <method name="RegisterStatusNotifierHost"><arg name="service" type="s" direction="in"/></method>
  <property name="RegisteredStatusNotifierItems" type="as" access="read"/>
  <property name="IsStatusNotifierHostRegistered" type="b" access="read"/>
  <property name="ProtocolVersion" type="i" access="read"/>
  <signal name="StatusNotifierItemRegistered"><arg type="s"/></signal>
  <signal name="StatusNotifierItemUnregistered"><arg type="s"/></signal>
  <signal name="StatusNotifierHostRegistered"/>
</interface></node>"""

fifo = sys.argv[1]
host_enabled = "--no-host" not in sys.argv
items = {}  # service string -> (bus name, path)
loop = GLib.MainLoop()
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)


def log(text):
    print(f"F {time.monotonic() * 1000:.3f} {text}", flush=True)


def item():
    return next(iter(items.values()), None)


def call(name, path, iface, method, args, reply=None):
    started = time.monotonic()
    try:
        result = bus.call_sync(name, path, iface, method, args, GLib.VariantType(reply) if reply else None, Gio.DBusCallFlags.NONE, 2000, None)
        return result, (time.monotonic() - started) * 1000
    except GLib.Error as error:
        log(f"call {iface}.{method} failed after {(time.monotonic() - started) * 1000:.1f} ms: {error.message}")
        return None, None


def describe(props):
    out = {}
    for key, value in props.items():
        if key.endswith("Pixmap"):
            out[key] = [f"{w}x{h} {len(data)}B sha1={hashlib.sha1(bytes(data)).hexdigest()[:8]}" for w, h, data in value]
        elif key == "ToolTip":
            out[key] = [value[0], len(value[1]), value[2], value[3]]
        else:
            out[key] = value
    return out


def read_props(reason):
    target = item()
    if not target:
        log("props: no item")
        return
    result, ms = call(target[0], target[1], "org.freedesktop.DBus.Properties", "GetAll", GLib.Variant("(s)", ("org.kde.StatusNotifierItem",)), "(a{sv})")
    if result is not None:
        log(f"props ({reason}) in {ms:.1f} ms: {describe(result.unpack()[0])}")


def on_item_signal(connection, sender, path, iface, signal, params):
    log(f"item signal {signal}{params.unpack() if params else ''}")
    if signal in ("NewIcon", "NewToolTip", "NewStatus", "NewTitle"):
        GLib.idle_add(lambda: (read_props("after " + signal), False)[1])


def on_item_vanished(connection, name):
    for service, (bus_name, _path) in list(items.items()):
        if bus_name == name:
            del items[service]
            log(f"item vanished {name}")
            bus.emit_signal(None, "/StatusNotifierWatcher", "org.kde.StatusNotifierWatcher", "StatusNotifierItemUnregistered", GLib.Variant("(s)", (service,)))


def watcher_call(connection, sender, path, iface, method, params, invocation):
    if method == "RegisterStatusNotifierItem":
        service = params.unpack()[0]
        bus_name, item_path = (sender, service) if service.startswith("/") else (service, "/StatusNotifierItem")
        items[service] = (bus_name, item_path)
        log(f"RegisterStatusNotifierItem service={service} sender={sender} -> {bus_name} {item_path}")
        invocation.return_value(None)
        bus.emit_signal(None, "/StatusNotifierWatcher", "org.kde.StatusNotifierWatcher", "StatusNotifierItemRegistered", GLib.Variant("(s)", (service,)))
        Gio.bus_watch_name_on_connection(bus, bus_name, Gio.BusNameWatcherFlags.NONE, None, on_item_vanished)
        if host_enabled:
            bus.signal_subscribe(bus_name, "org.kde.StatusNotifierItem", None, item_path, None, Gio.DBusSignalFlags.NONE, on_item_signal)
            bus.signal_subscribe(bus_name, "com.canonical.dbusmenu", None, None, None, Gio.DBusSignalFlags.NONE, on_item_signal)
            GLib.idle_add(lambda: (read_props("registration"), False)[1])
    elif method == "RegisterStatusNotifierHost":
        invocation.return_value(None)
    else:
        invocation.return_dbus_error("org.freedesktop.DBus.Error.UnknownMethod", method)


def watcher_property(connection, sender, path, iface, name):
    if name == "RegisteredStatusNotifierItems":
        return GLib.Variant("as", list(items.keys()))
    if name == "IsStatusNotifierHostRegistered":
        return GLib.Variant("b", host_enabled)
    if name == "ProtocolVersion":
        return GLib.Variant("i", 0)
    return None


def menu_layout():
    target = item()
    if not target:
        return None
    props, _ = call(target[0], target[1], "org.freedesktop.DBus.Properties", "Get", GLib.Variant("(ss)", ("org.kde.StatusNotifierItem", "Menu")), "(v)")
    menu_path = props.unpack()[0] if props else "/MenuBar"
    shown, ms1 = call(target[0], menu_path, "com.canonical.dbusmenu", "AboutToShow", GLib.Variant("(i)", (0,)), "(b)")
    layout, ms2 = call(target[0], menu_path, "com.canonical.dbusmenu", "GetLayout", GLib.Variant("(iias)", (0, -1, [])), "(u(ia{sv}av))")
    if layout is None:
        return None
    revision, (root_id, root_props, children) = layout.unpack()
    entries = [(child[0], child[1].get("label"), child[1].get("type")) for child in children]
    log(f"menu AboutToShow={shown.unpack() if shown else None} ({ms1:.1f} ms) GetLayout rev={revision} root={root_props} items={entries} ({ms2:.1f} ms)")
    return target[0], menu_path, entries


def command(line):
    parts = line.split(" ", 1)
    name, argument = parts[0], (parts[1] if len(parts) > 1 else "")
    target = item()
    log(f"command {line}")
    if name == "props":
        read_props("command")
    elif name in ("activate", "secondary", "contextmenu") and target:
        method = {"activate": "Activate", "secondary": "SecondaryActivate", "contextmenu": "ContextMenu"}[name]
        _, ms = call(target[0], target[1], "org.kde.StatusNotifierItem", method, GLib.Variant("(ii)", (100, 100)))
        log(f"{method} returned in {ms:.1f} ms" if ms is not None else f"{method} failed")
    elif name == "menu":
        menu_layout()
    elif name == "click":
        found = menu_layout()
        if found:
            bus_name, path, entries = found
            ids = [entry[0] for entry in entries if entry[1] == argument]
            if ids:
                _, ms = call(bus_name, path, "com.canonical.dbusmenu", "Event", GLib.Variant("(isvu)", (ids[0], "clicked", GLib.Variant("i", 0), int(time.time()) & 0xFFFFFFFF)))
                log(f"Event clicked id={ids[0]} ({argument}) returned in {ms:.1f} ms" if ms is not None else "Event failed")
    elif name == "quit":
        loop.quit()
    return False


def reader():
    while True:
        with open(fifo) as stream:
            for line in stream:
                line = line.strip()
                if line:
                    GLib.idle_add(command, line)


node = Gio.DBusNodeInfo.new_for_xml(WATCHER_XML)
bus.register_object("/StatusNotifierWatcher", node.interfaces[0], watcher_call, watcher_property, None)
started = time.monotonic()


def acquired(connection, name):
    log(f"watcher owns {name} after {(time.monotonic() - started) * 1000:.1f} ms (host={'on' if host_enabled else 'off'})")


Gio.bus_own_name_on_connection(bus, "org.kde.StatusNotifierWatcher", Gio.BusNameOwnerFlags.NONE, acquired, lambda c, n: log(f"lost {n}"))
threading.Thread(target=reader, daemon=True).start()
loop.run()
log("watcher exit")
