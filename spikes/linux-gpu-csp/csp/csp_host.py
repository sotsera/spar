#!/usr/bin/env python3
"""connect-src probe host: WebKitGTK 6.0 (Python GI), the page on spar://app, CSP sent as a response header.

Usage: python3 -I csp_host.py <pages> <out.json> --profile spar|self|none [--label L]
Run under an Xvfb of the caller's (DISPLAY set, WAYLAND_DISPLAY unset); the window is presented there.

Loopback TCP listeners, one per probe target, count every accepted connection and keep its first bytes, so "refused
by CSP" can be checked as "0 connections". The scheme handler records every request it sees (method, URI, body size),
including CSP reports to /_spar/csp-report.
"""
import argparse
import json
import os
import socket
import sys
import threading
import time

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("WebKit", "6.0")
gi.require_version("Soup", "3.0")
from gi.repository import Gio, GLib, Gtk, Soup, WebKit  # noqa: E402

parser = argparse.ArgumentParser()
parser.add_argument("pages")
parser.add_argument("out")
parser.add_argument("--profile", choices=["spar", "self", "none"], required=True)
parser.add_argument("--label", default="")
args = parser.parse_args()

BASE = ("default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
        "connect-src {connect}; media-src 'self'; worker-src 'self'; manifest-src 'self'; "
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; "
        "require-trusted-types-for 'script'; report-uri /_spar/csp-report")
CSP = None if args.profile == "none" else BASE.format(connect="spar://app" if args.profile == "spar" else "'self'")
TYPES = {".html": "text/html", ".js": "text/javascript", ".json": "application/json"}
TARGETS = ["ws", "wss", "wsLocalhost", "fetch", "fetchLocalhost", "xhr", "es", "beacon", "wfetch", "wws", "wxhr"]

started = time.monotonic()
state = {"requests": [], "reports": [], "page": None, "done": False}
lock = threading.Lock()


class Listener:
    """Accepts connections on 127.0.0.1, counts them, answers HTTP with a CORS-open 200 (for the no-CSP control)."""

    def __init__(self, name):
        self.name = name
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("127.0.0.1", 0))
        self.sock.listen(16)
        self.port = self.sock.getsockname()[1]
        self.connections = []
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try:
                conn, _ = self.sock.accept()
            except OSError:
                return
            conn.settimeout(1.0)
            first = b""
            try:
                first = conn.recv(512)
                if first.startswith((b"GET", b"POST", b"OPTIONS")):
                    if b"text/event-stream" in first:
                        body = b"data: loopback\n\n"
                        head = b"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\n"
                    else:
                        body = b"loopback"
                        head = b"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n"
                    conn.sendall(head + b"Access-Control-Allow-Origin: *\r\nContent-Length: " +
                                 str(len(body)).encode() + b"\r\nConnection: close\r\n\r\n" + body)
            except OSError:
                pass
            finally:
                conn.close()
            with lock:
                self.connections.append({"t": round((time.monotonic() - started) * 1000), "first": first[:80].decode("latin-1")})


listeners = {name: Listener(name) for name in TARGETS}


def respond(request, status, content_type, body, extra=None):
    stream = Gio.MemoryInputStream.new_from_bytes(GLib.Bytes.new(body))
    response = WebKit.URISchemeResponse.new(stream, len(body))
    response.set_status(status, None)
    response.set_content_type(content_type)
    headers = Soup.MessageHeaders.new(Soup.MessageHeadersType.RESPONSE)
    # Content-Type is repeated in the header set, or WebKit blocks scripts under nosniff (pitfalls).
    headers.append("Content-Type", content_type)
    headers.append("X-Content-Type-Options", "nosniff")
    headers.append("Cache-Control", "no-store")
    if CSP is not None:
        headers.append("Content-Security-Policy", CSP)
    for key, value in (extra or {}).items():
        headers.append(key, value)
    response.set_http_headers(headers)
    request.finish_with_response(response)


def read_body(request):
    stream = request.get_http_body()
    if stream is None:
        return b""
    data = b""
    while True:
        chunk = stream.read_bytes(65536, None).get_data()
        if not chunk:
            return data
        data += chunk


def on_scheme(request):
    uri = GLib.Uri.parse(request.get_uri(), GLib.UriFlags.NONE)
    host, path = uri.get_host(), uri.get_path()
    method = request.get_http_method()
    body = read_body(request) if method == "POST" else b""
    with lock:
        state["requests"].append({"t": round((time.monotonic() - started) * 1000), "method": method,
                                  "uri": request.get_uri(), "bodyBytes": len(body)})
    if path == "/_spar/csp-report":
        with lock:
            state["reports"].append(body.decode("utf-8", "replace"))
        return respond(request, 204, "text/plain", b"")
    if host != "app":
        return respond(request, 404, "text/plain", b"wrong host")
    if path == "/beacon":
        return respond(request, 204, "text/plain", b"")
    if path == "/events":
        return respond(request, 200, "text/event-stream", b"data: scheme\n\n")
    if path == "/index.html":
        ports = "&".join(f"{n}={listener.port}" for n, listener in listeners.items())
        name = "index.html"
    else:
        name = path.lstrip("/")
    full = os.path.join(args.pages, name)
    if "/" in name or not os.path.isfile(full):
        return respond(request, 404, "text/plain", b"not found")
    with open(full, "rb") as handle:
        content = handle.read()
    if name == "index.html":
        # The listener ports reach the page in a meta element (no inline script under the CSP).
        content = content.replace(b"__PORTS__", ports.encode())
    respond(request, 200, TYPES.get(os.path.splitext(name)[1], "application/octet-stream"), content)


def on_message(_manager, value):
    message = json.loads(value.to_string())
    if message.get("type") == "done":
        state["page"] = message
        # Late connections, beacons and reports get a second to arrive.
        GLib.timeout_add(1500, finish)
    elif message.get("type") == "log":
        print("[page]", message.get("text"), flush=True)


def finish():
    if state["done"]:
        return False
    state["done"] = True
    with lock:
        result = {
            "label": args.label, "profile": args.profile, "csp": CSP,
            "webkit": f"{WebKit.get_major_version()}.{WebKit.get_minor_version()}.{WebKit.get_micro_version()}",
            "gtk": f"{Gtk.get_major_version()}.{Gtk.get_minor_version()}.{Gtk.get_micro_version()}",
            "elapsedMs": round((time.monotonic() - started) * 1000),
            "listeners": {n: {"port": listener.port, "connections": listener.connections} for n, listener in listeners.items()},
            "requests": state["requests"], "reports": state["reports"], "page": state["page"],
        }
    with open(args.out, "w") as handle:
        json.dump(result, handle, indent=1)
    window.destroy()
    # Keep the loop running until the web processes have gone (pitfalls), then stop.
    GLib.timeout_add(500, loop.quit)
    return False


def on_timeout():
    sys.stderr.write("timeout: page never reported\n")
    finish()
    return False


Gtk.init()
context = WebKit.WebContext()
context.register_uri_scheme("spar", on_scheme)
security = context.get_security_manager()
security.register_uri_scheme_as_secure("spar")
security.register_uri_scheme_as_cors_enabled("spar")
manager = WebKit.UserContentManager()
manager.register_script_message_handler("probe", None)
manager.connect("script-message-received::probe", on_message)
session = WebKit.NetworkSession.new_ephemeral()
view = WebKit.WebView(web_context=context, network_session=session, user_content_manager=manager)
view.get_settings().set_enable_write_console_messages_to_stdout(True)
window = Gtk.Window(title="csp-probe")
window.set_default_size(800, 600)
window.set_child(view)
view.load_uri("spar://app/index.html")
window.present()
loop = GLib.MainLoop()
GLib.timeout_add_seconds(60, on_timeout)
loop.run()
print(f"{args.label}: done in {round((time.monotonic() - started) * 1000)} ms", flush=True)
