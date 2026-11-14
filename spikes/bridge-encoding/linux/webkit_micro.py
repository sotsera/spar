"""Runs the page's micro benchmark (mode=micro) in WebKitGTK (GTK 3, WebKit2 4.1) inside a Gtk.OffscreenWindow.

Usage: python3 -I webkit_micro.py <bench.html> <output.json>
The page posts its result through the "bench" script message handler; nothing is shown on screen.
"""
import sys

import gi

gi.require_version("Gtk", "3.0")
gi.require_version("WebKit2", "4.1")
from gi.repository import GLib, Gtk, WebKit2  # noqa: E402

page, output = sys.argv[1], sys.argv[2]
state = {"code": 1}


def on_message(_manager, result):
    value = result.get_js_value() if hasattr(result, "get_js_value") else result
    with open(output, "w", encoding="utf-8") as handle:
        handle.write(value.to_string())
    state["code"] = 0
    Gtk.main_quit()


def on_timeout():
    sys.stderr.write("timeout\n")
    Gtk.main_quit()
    return False


manager = WebKit2.UserContentManager()
manager.register_script_message_handler("bench")
manager.connect("script-message-received::bench", on_message)
context = WebKit2.WebContext.new_ephemeral()
view = WebKit2.WebView(web_context=context, user_content_manager=manager)
settings = view.get_settings()
settings.set_allow_file_access_from_file_urls(True)
window = Gtk.OffscreenWindow()
window.set_default_size(800, 600)
window.add(view)
window.show_all()
view.load_uri(GLib.filename_to_uri(page, None) + "?mode=micro")
GLib.timeout_add_seconds(180, on_timeout)
Gtk.main()
print("webkit", WebKit2.get_major_version(), WebKit2.get_minor_version(), WebKit2.get_micro_version(), "exit", state["code"])
sys.exit(state["code"])
