"""Runs the page benchmark in WebKitGTK 6.0 (GTK 4) and writes the page's report as JSON.

Usage: python3 -I webkit_bench.py <bench.html> <output.json> [extra query, e.g. quick=1 or mode=memory&variant=subclass&style=none]
The web view is never put in a presented window, so nothing appears on screen. The page reports through the "bench"
script message handler. It asks for its own resident memory through the "rss" handler (JSC has no heap counter for
pages); the answer is the summed VmRSS of the WebKitWebProcess processes started by this Python process, sent back
with evaluate_javascript.
"""
import json
import os
import sys

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("WebKit", "6.0")
from gi.repository import GLib, Gtk, WebKit  # noqa: E402

page, output = sys.argv[1], sys.argv[2]
extra = sys.argv[3] if len(sys.argv) > 3 else ""
state = {"code": 1}
me = os.getpid()


def parent_of(pid):
    try:
        with open(f"/proc/{pid}/stat", encoding="utf-8") as handle:
            return int(handle.read().rsplit(")", 1)[1].split()[1])
    except (OSError, ValueError, IndexError):
        return 0


def descends_from_me(pid):
    for _ in range(16):
        pid = parent_of(pid)
        if pid == me:
            return True
        if pid <= 1:
            return False
    return False


def web_process_rss():
    total, found = 0, 0
    for entry in os.listdir("/proc"):
        if not entry.isdigit():
            continue
        try:
            with open(f"/proc/{entry}/cmdline", "rb") as handle:
                argv0 = handle.read().split(b"\0", 1)[0]
            if not argv0.endswith(b"WebKitWebProcess") or not descends_from_me(int(entry)):
                continue
            with open(f"/proc/{entry}/status", encoding="utf-8") as handle:
                for line in handle:
                    if line.startswith("VmRSS:"):
                        total += int(line.split()[1]) * 1024
                        found += 1
        except OSError:
            continue
    state["webProcesses"] = found
    return total


def on_result(_manager, value):
    report = json.loads(value.to_string())
    details = report.setdefault("details", {})
    details["webkitgtk"] = f"{WebKit.get_major_version()}.{WebKit.get_minor_version()}.{WebKit.get_micro_version()}"
    details["gtk"] = f"{Gtk.get_major_version()}.{Gtk.get_minor_version()}.{Gtk.get_micro_version()}"
    details["webProcessesMeasured"] = state.get("webProcesses", 0)
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=1)
    state["code"] = 0 if "error" not in report else 3
    loop.quit()


def on_rss(_manager, _value):
    view.evaluate_javascript(f"window.__rssReply({web_process_rss()})", -1, None, None, None, None, None)


def on_timeout():
    sys.stderr.write("timeout\n")
    loop.quit()
    return False


manager = WebKit.UserContentManager()
manager.register_script_message_handler("bench", None)
manager.register_script_message_handler("rss", None)
manager.connect("script-message-received::bench", on_result)
manager.connect("script-message-received::rss", on_rss)
session = WebKit.NetworkSession.new_ephemeral()
view = WebKit.WebView(network_session=session, user_content_manager=manager)
view.get_settings().set_allow_file_access_from_file_urls(True)
loop = GLib.MainLoop()
view.load_uri(GLib.filename_to_uri(page, None) + "?engine=webkitgtk" + ("&" + extra if extra else ""))
GLib.timeout_add_seconds(900, on_timeout)
loop.run()
print("webkitgtk", WebKit.get_major_version(), WebKit.get_minor_version(), WebKit.get_micro_version(), "exit", state["code"])
sys.exit(state["code"])
