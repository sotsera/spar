# The page's accessibility tree as the platform exposes it: WebKitGTK's web process over AT-SPI (D-Bus), read with
# libatspi through GObject introspection. Usage: python3 -I atspi.py <harness log> <hold prefix> <out dir>
# For each state the harness announces with "HOLD <state>", it walks the tree under the document, writes
# tree-atspi-<state>.json, times a full walk and a Collection query, then releases the harness.
import json, os, sys, time
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

log, hold, out = sys.argv[1], sys.argv[2], sys.argv[3]

def find_document():
    desktop = Atspi.get_desktop(0)
    for i in range(desktop.get_child_count()):
        app = desktop.get_child_at_index(i)
        if app is None:
            continue
        stack = [app]
        while stack:
            node = stack.pop()
            try:
                role = node.get_role_name()
            except Exception:
                continue
            if role == "document web":
                return app, node
            for j in range(min(node.get_child_count(), 50)):
                child = node.get_child_at_index(j)
                if child is not None:
                    stack.append(child)
    return None, None

def walk(node, depth=0, out=None):
    out = [] if out is None else out
    try:
        attributes = node.get_attributes() or {}
        out.append({"role": node.get_role_name(), "name": node.get_name() or "", "depth": depth,
                    "tag": attributes.get("tag", ""), "id": attributes.get("id", ""), "xml-roles": attributes.get("xml-roles", "")})
        for i in range(node.get_child_count()):
            child = node.get_child_at_index(i)
            if child is not None:
                walk(child, depth + 1, out)
    except Exception as e:
        out.append({"error": str(e)})
    return out

def report(state):
        started = time.perf_counter()
        app, document = find_document()
        found_ms = (time.perf_counter() - started) * 1000
        if document is None:
            print(f"ATSPI {state}: no 'document web' node found in {found_ms:.1f} ms", flush=True)
        else:
            started = time.perf_counter()
            nodes = walk(document)
            walk_ms = (time.perf_counter() - started) * 1000
            json.dump(nodes, open(os.path.join(out, f"tree-atspi-{state}.json"), "w"), indent=0)
            # One query the way a locator would ask: by role (WebKit maps the menu button to "combo box"), then the name.
            started = time.perf_counter()
            rule = Atspi.MatchRule.new(Atspi.StateSet.new([]), Atspi.CollectionMatchType.ALL, {}, Atspi.CollectionMatchType.ALL,
                                       [Atspi.Role.PUSH_BUTTON, Atspi.Role.COMBO_BOX], Atspi.CollectionMatchType.ANY, [], Atspi.CollectionMatchType.ALL, False)
            matches = document.get_collection_iface().get_matches(rule, Atspi.CollectionSortOrder.CANONICAL, 0, True)
            named = [m for m in matches if "Actions" in (m.get_name() or "")]
            query_ms = (time.perf_counter() - started) * 1000
            extents = named[0].get_component_iface().get_extents(Atspi.CoordType.SCREEN) if named else None
            print(f"ATSPI {state}: app={app.get_name()!r} document found in {found_ms:.1f} ms; walk {len(nodes)} nodes in {walk_ms:.1f} ms; "
                  f"Collection query buttons={len(matches)} 'Actions'={len(named)} in {query_ms:.1f} ms; "
                  f"'Actions' screen extents={(extents.x, extents.y, extents.width, extents.height) if extents else None}", flush=True)

seen = set()
deadline = time.time() + 120
while time.time() < deadline:
    try:
        lines = open(log).read().splitlines()
    except OSError:
        lines = []
    if any(l.startswith("RUN exit") or l.startswith("FATAL") for l in lines):
        break
    for state in [l.split()[1] for l in lines if l.startswith("HOLD ")]:
        if state in seen:
            continue
        seen.add(state)
        try:
            report(state)
        except Exception as e:
            print(f"ATSPI {state}: failed: {e!r}", flush=True)
        open(f"{hold}-{state}", "w").close()
    time.sleep(0.05)
