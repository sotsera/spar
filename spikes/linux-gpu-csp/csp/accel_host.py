#!/usr/bin/env python3
"""Does the software-GPU default take effect? WebKitGTK 6.0 (Python GI) under the caller's Xvfb.

Usage: python3 -I accel_host.py <out.json> --policy default|never|always [--label L]
GSK_RENDERER and WEBKIT_DISABLE_DMABUF_RENDERER come from the caller's environment (set before this process
starts, as the host's libc setenv before GTK would). Presents a window, loads webkit://gpu, reads its text through
JavaScript, the GSK renderer GTK picked for the window, and the private memory of this process and its WebKit
descendants (statm resident - shared).
"""
import argparse
import json
import os
import sys
import time

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("WebKit", "6.0")
from gi.repository import GLib, GObject, Gtk, WebKit  # noqa: E402

parser = argparse.ArgumentParser()
parser.add_argument("out")
parser.add_argument("--policy", choices=["default", "never", "always"], default="default")
parser.add_argument("--label", default="")
parser.add_argument("--uri", default="webkit://gpu")
args = parser.parse_args()
started = time.monotonic()
PAGE = os.sysconf("SC_PAGE_SIZE")
result = {"label": args.label, "policy": args.policy, "uri": args.uri,
          "env": {k: os.environ.get(k) for k in ["GSK_RENDERER", "WEBKIT_DISABLE_DMABUF_RENDERER", "GDK_BACKEND",
                                                 "LIBGL_ALWAYS_SOFTWARE", "GALLIUM_DRIVER"]}}


def ms():
    return round((time.monotonic() - started) * 1000, 1)


def children(pid):
    out = []
    try:
        for task in os.listdir(f"/proc/{pid}/task"):
            with open(f"/proc/{pid}/task/{task}/children") as handle:
                out += [int(c) for c in handle.read().split()]
    except OSError:
        pass
    return out


def memory():
    rows, todo = [], [os.getpid()]
    while todo:
        pid = todo.pop()
        try:
            with open(f"/proc/{pid}/comm") as handle:
                comm = handle.read().strip()
            with open(f"/proc/{pid}/statm") as handle:
                fields = handle.read().split()
            rows.append({"pid": pid, "comm": comm, "privateMb": round((int(fields[1]) - int(fields[2])) * PAGE / 2**20, 1),
                         "rssMb": round(int(fields[1]) * PAGE / 2**20, 1)})
        except OSError:
            pass
        todo += children(pid)
    return rows


def on_text(view, task):
    try:
        value = view.evaluate_javascript_finish(task)
        result["pageText"] = value.to_string()
    except GLib.Error as e:
        result["pageTextError"] = e.message
    GLib.timeout_add(1500, finish)


def on_load(view, event):
    if event == WebKit.LoadEvent.FINISHED:
        result["loadFinishedMs"] = ms()
        view.evaluate_javascript("document.body ? document.body.innerText : document.documentElement.innerText",
                                 -1, None, None, None, on_text)


def finish():
    renderer = window.get_renderer()
    result["gskRenderer"] = GObject.type_name(renderer.__gtype__) if renderer else None
    result["memory"] = memory()
    result["privateMbTotal"] = round(sum(r["privateMb"] for r in result["memory"]), 1)
    result["webkit"] = f"{WebKit.get_major_version()}.{WebKit.get_minor_version()}.{WebKit.get_micro_version()}"
    result["settingsPolicy"] = view.get_settings().get_hardware_acceleration_policy().value_nick
    with open(args.out, "w") as handle:
        json.dump(result, handle, indent=1)
    window.destroy()
    GLib.timeout_add(500, loop.quit)
    return False


def on_timeout():
    result["error"] = "timeout"
    finish()
    return False


Gtk.init()
session = WebKit.NetworkSession.new_ephemeral()
view = WebKit.WebView(network_session=session)
if args.policy != "default":
    view.get_settings().set_hardware_acceleration_policy(
        WebKit.HardwareAccelerationPolicy.NEVER if args.policy == "never" else WebKit.HardwareAccelerationPolicy.ALWAYS)
view.connect("load-changed", on_load)
window = Gtk.Window(title="accel-probe")
window.set_default_size(1000, 800)
window.set_child(view)
view.load_uri(args.uri)
window.present()
loop = GLib.MainLoop()
GLib.timeout_add_seconds(40, on_timeout)
loop.run()
print(f"{args.label}: gsk={result.get('gskRenderer')} private={result.get('privateMbTotal')} MB in {ms()} ms", flush=True)
sys.exit(0)
