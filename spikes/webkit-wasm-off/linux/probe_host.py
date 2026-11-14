"""Minimal WebKitGTK 6.0 host for the WebAssembly probe.

Usage:
  python3 -I probe_host.py <pages-dir> <output.json> [options]
  python3 -I probe_host.py --dump-features <output.json>

Options:
  --profile NAME          CSP profile (none, base, wasm-unsafe-eval, unsafe-eval); default base
  --origin spar|http      serve from the custom scheme spar://app (default) or a loopback HTTP server
  --user-script MODE      none, delete or lock: a document-start user script for all frames
  --ext-dir DIR           web process extensions directory; --ext-mode off|report is passed as user data
  --setenv NAME=VALUE     set in this (UI) process environment before WebKit starts (repeatable)
  --jsc-ui NAME=BOOL      jsc_options_set_boolean in the UI process before WebKit starts (repeatable)

The web view is never put in a presented window, so nothing appears on screen. The page reports through the
"probe" script message handler. Timers in the page are throttled for a hidden view, so the host calls
window.__hostTick() every 1.5 s to end any wait that has no event.
"""
import argparse
import json
import os
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

parser = argparse.ArgumentParser()
parser.add_argument("pages", nargs="?")
parser.add_argument("output")
parser.add_argument("--dump-features", action="store_true")
parser.add_argument("--profile", default="base")
parser.add_argument("--origin", default="spar", choices=["spar", "http"])
parser.add_argument("--user-script", default="none", choices=["none", "delete", "lock"])
parser.add_argument("--ext-dir")
parser.add_argument("--ext-mode", default="report")
parser.add_argument("--ext-early", action="store_true", help="set the extensions directory before loading, not in the signal")
parser.add_argument("--setenv", action="append", default=[])
parser.add_argument("--jsc-ui", action="append", default=[])
parser.add_argument("--label", default="")
args = parser.parse_args()

for item in args.setenv:
    name, value = item.split("=", 1)
    os.environ[name] = value  # os.environ assignment calls putenv, so spawned processes inherit it

import gi  # noqa: E402

gi.require_version("Gtk", "4.0")
gi.require_version("WebKit", "6.0")
gi.require_version("Soup", "3.0")
gi.require_version("JavaScriptCore", "6.0")
from gi.repository import Gio, GLib, Gtk, JavaScriptCore, Soup, WebKit  # noqa: E402

for item in args.jsc_ui:
    name, value = item.split("=", 1)
    JavaScriptCore.options_set_boolean(name, value.lower() == "true")

BASE = (
    "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
    "connect-src 'self'; worker-src 'self'; manifest-src 'self'; "
    "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; "
    "report-uri /_spar/csp-report"
)
PROFILES = {
    "none": None,
    "base": BASE,
    "wasm-unsafe-eval": BASE.replace("script-src 'self';", "script-src 'self' 'wasm-unsafe-eval';"),
    "unsafe-eval": BASE.replace("script-src 'self';", "script-src 'self' 'unsafe-eval';"),
}
TYPES = {".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".wasm": "application/wasm"}
ADD_WASM = bytes([
    0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00, 0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
    0x03, 0x02, 0x01, 0x00, 0x07, 0x07, 0x01, 0x03, 0x61, 0x64, 0x64, 0x00, 0x00,
    0x0A, 0x09, 0x01, 0x07, 0x00, 0x20, 0x00, 0x20, 0x01, 0x6A, 0x0B,
])
USER_SCRIPTS = {
    "delete": "delete globalThis.WebAssembly; Object.defineProperty(globalThis, '__sparWasmOff', { value: 'delete' });",
    "lock": (
        "Object.defineProperty(globalThis, 'WebAssembly', { value: undefined, writable: false, enumerable: false, configurable: false });"
        " Object.defineProperty(globalThis, '__sparWasmOff', { value: 'lock' });"
    ),
}

me = os.getpid()
state = {"code": 1, "progress": [], "served": [], "cspReports": 0, "processes": {}}
t_start = time.monotonic()


def elapsed_ms():
    return round((time.monotonic() - t_start) * 1000, 1)


def route(path, query):
    """Returns (status, content type, body, headers) for a request path."""
    csp = PROFILES[args.profile]
    if path == "/_spar/csp-report":
        state["cspReports"] += 1
        return 204, "text/plain", b"", {}
    name = path.lstrip("/") or "probe.html"
    if name == "add.wasm":
        body = ADD_WASM
    else:
        full = os.path.join(args.pages, name)
        if "/" in name or not os.path.isfile(full):
            return 404, "text/plain", b"not found", {}
        with open(full, "rb") as handle:
            body = handle.read()
    content_type = TYPES.get(os.path.splitext(name)[1], "application/octet-stream")
    headers = {"Content-Type": content_type}
    send_csp = csp is not None and not (name == "worker.js" and query.get("csp") == ["0"])
    if send_csp:
        headers["Content-Security-Policy"] = csp
    state["served"].append(f"{name}{'?' + '&'.join(f'{k}={v[0]}' for k, v in query.items()) if query else ''} csp={int(send_csp)}")
    return 200, content_type, body, headers


def on_scheme(request):
    uri = GLib.Uri.parse(request.get_uri(), GLib.UriFlags.NONE)
    query = parse_qs(uri.get_query() or "")
    status, content_type, body, headers = route(uri.get_path(), query)
    stream = Gio.MemoryInputStream.new_from_bytes(GLib.Bytes.new(body))
    response = WebKit.URISchemeResponse.new(stream, len(body))
    response.set_status(status, None)
    response.set_content_type(content_type)
    soup_headers = Soup.MessageHeaders.new(Soup.MessageHeadersType.RESPONSE)
    for key, value in headers.items():
        soup_headers.append(key, value)
    response.set_http_headers(soup_headers)
    request.finish_with_response(response)


class Handler(BaseHTTPRequestHandler):
    def _serve(self):
        parts = urlsplit(self.path)
        status, content_type, body, headers = route(parts.path, parse_qs(parts.query))
        self.send_response(status)
        for key, value in headers.items():
            self.send_header(key, value)
        if "Content-Type" not in headers:
            self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):  # noqa: N802
        self._serve()

    def do_POST(self):  # noqa: N802
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)
        self._serve()

    def log_message(self, *_):
        pass


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


def snapshot_processes():
    """Records WebKit child processes and the JSC_* entries of their environment."""
    for entry in os.listdir("/proc"):
        if not entry.isdigit() or not descends_from_me(int(entry)):
            continue
        try:
            with open(f"/proc/{entry}/cmdline", "rb") as handle:
                argv0 = os.path.basename(handle.read().split(b"\0", 1)[0].decode(errors="replace"))
        except OSError:
            continue
        if not argv0.startswith("WebKit"):
            continue
        info = {"process": argv0}
        try:
            with open(f"/proc/{entry}/environ", "rb") as handle:
                env = [e.decode(errors="replace") for e in handle.read().split(b"\0") if e]
            info["jscEnv"] = sorted(e for e in env if e.startswith("JSC_"))
            info["envCount"] = len(env)
        except OSError as error:
            info["environError"] = f"{type(error).__name__}: {error}"
        state["processes"][entry] = info


def jsc_ui_value(name):
    ok, value = JavaScriptCore.options_get_boolean(name)
    return value if ok else "missing"


def dump_features():
    settings = WebKit.Settings()
    props = sorted(p.name for p in settings.list_properties())
    features = []
    feature_list = WebKit.Settings.get_all_features()
    for i in range(feature_list.get_length()):
        f = feature_list.get(i)
        features.append({
            "identifier": f.get_identifier(), "name": f.get_name(), "category": f.get_category(),
            "status": f.get_status().value_nick, "defaultValue": f.get_default_value(),
            "enabled": settings.get_feature_enabled(f),
        })
    keywords = ("wasm", "webassembly", "jit", "eval", "javascript")
    jsc = []

    def each(name, option_type, description):
        if any(k in name.lower() for k in ("wasm", "webassembly", "jit", "bbq", "omg", "ipint")):
            entry = {"name": name, "type": option_type.value_nick, "description": description}
            if option_type == JavaScriptCore.OptionType.BOOLEAN:
                entry["uiValue"] = jsc_ui_value(name)
            jsc.append(entry)
        return False

    JavaScriptCore.options_foreach(each)
    report = {
        "webkitgtk": f"{WebKit.get_major_version()}.{WebKit.get_minor_version()}.{WebKit.get_micro_version()}",
        "settingsPropertiesMatching": [p for p in props if any(k in p for k in keywords)],
        "settingsPropertyCount": len(props),
        "featureCount": len(features),
        "featuresMatching": [f for f in features if any(k in (f["identifier"] + " " + (f["name"] or "")).lower() for k in keywords)],
        "jscOptionsMatching": jsc,
    }
    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=1)
    print("features", len(features), "settings", len(props), "jsc options matched", len(jsc))


if args.dump_features:
    dump_features()
    sys.exit(0)


def finish(report):
    snapshot_processes()
    out = {
        "label": args.label,
        "profile": args.profile,
        "origin": args.origin,
        "userScript": args.user_script,
        "extension": (args.ext_mode + (" early" if args.ext_early else " in-signal")) if args.ext_dir else None,
        "setenv": args.setenv,
        "jscUi": args.jsc_ui,
        "uiProcessUseWasm": jsc_ui_value("useWasm"),
        "webkitgtk": f"{WebKit.get_major_version()}.{WebKit.get_minor_version()}.{WebKit.get_micro_version()}",
        "gtk": f"{Gtk.get_major_version()}.{Gtk.get_minor_version()}.{Gtk.get_micro_version()}",
        "hostMs": {"reportAt": elapsed_ms(), "progress": state["progress"]},
        "served": state["served"],
        "cspReportsReceived": state["cspReports"],
        "webkitProcesses": list(state["processes"].values()),
        "report": report,
    }
    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(out, handle, indent=1)
    state["code"] = 0 if report is not None and "fatal" not in report else 3
    loop.quit()


def on_message(_manager, value):
    message = json.loads(value.to_string())
    if "progress" in message:
        state["progress"].append([message["progress"], elapsed_ms()])
        if message["progress"] == "start":
            snapshot_processes()
        return
    finish(message["report"])


def on_tick():
    view.evaluate_javascript("window.__hostTick && window.__hostTick()", -1, None, None, None, None, None)
    return True


def on_timeout():
    sys.stderr.write("timeout\n")
    finish(None)
    return False


def on_init_extensions(context):
    if args.ext_dir:
        print(f"[host] initialize-web-process-extensions: {args.ext_dir} mode={args.ext_mode} early={args.ext_early}", flush=True)
        if not args.ext_early:
            context.set_web_process_extensions_directory(args.ext_dir)
        context.set_web_process_extensions_initialization_user_data(GLib.Variant("s", args.ext_mode))


context = WebKit.WebContext()
if args.ext_dir and args.ext_early:
    # Set before anything loads: the setter also adds the directory to the sandbox, and the sandbox is
    # built when the web process is launched, before the signal below is emitted.
    context.set_web_process_extensions_directory(args.ext_dir)
context.connect("initialize-web-process-extensions", on_init_extensions)
context.register_uri_scheme("spar", on_scheme)
security = context.get_security_manager()
security.register_uri_scheme_as_secure("spar")
security.register_uri_scheme_as_cors_enabled("spar")

manager = WebKit.UserContentManager()
manager.register_script_message_handler("probe", None)
manager.connect("script-message-received::probe", on_message)
if args.user_script != "none":
    manager.add_script(WebKit.UserScript.new(
        USER_SCRIPTS[args.user_script], WebKit.UserContentInjectedFrames.ALL_FRAMES,
        WebKit.UserScriptInjectionTime.START, None, None))

session = WebKit.NetworkSession.new_ephemeral()
view = WebKit.WebView(web_context=context, network_session=session, user_content_manager=manager)
view.get_settings().set_enable_write_console_messages_to_stdout(True)
view.connect("create", lambda *_: None)

if args.origin == "http":
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    url = f"http://127.0.0.1:{server.server_address[1]}/probe.html"
else:
    url = "spar://app/probe.html"

loop = GLib.MainLoop()
view.load_uri(url + "?label=" + GLib.Uri.escape_string(args.label, None, False))
GLib.timeout_add(1500, on_tick)
GLib.timeout_add_seconds(90, on_timeout)
loop.run()
print(f"{args.label}: exit {state['code']} in {elapsed_ms()} ms")
sys.exit(state["code"])
