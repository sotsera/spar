#!/usr/bin/env python3
"""Tables from the probe runs. Usage: python3 -I summarize.py <results-dir>...
For each <dir>: series.jsonl and gdk.jsonl -> detection table; csp/*.json -> connect-src table; accel/*.json -> accel
table. Writes <dir>/summary.md and prints it.
"""
import glob
import json
import os
import statistics
import sys


def stats(values):
    values = sorted(v for v in values if v is not None)
    if not values:
        return "-"
    return f"{statistics.median(values):.2f} ({values[0]:.2f}–{values[-1]:.2f})"


def detection(path):
    rows = [json.loads(line) for line in open(path)]
    out = ["| Method | Kind | n | Detect ms median (range) | Process wall ms median | RSS growth MB median | Verdict | Renderer / reason |",
           "|---|---|---|---|---|---|---|---|"]
    keys = []
    for r in rows:
        k = (r["method"], r["kind"])
        if k not in keys:
            keys.append(k)
    for method, kind in keys:
        sel = [r for r in rows if r["method"] == method and r["kind"] == kind]
        verdicts = sorted({r.get("verdict", "?") for r in sel})
        renderer = sel[-1].get("glRenderer") or sel[-1].get("reason") or ""
        if sel[-1].get("windowRenderer"):
            renderer += f"; window: {sel[-1]['windowRenderer']}"
        if sel[-1].get("driverName"):
            renderer += f"; EGL driver {sel[-1]['driverName']}"
        rss = [(r.get("rssKbAfter", 0) - r.get("rssKbBefore", 0)) / 1024 for r in sel]
        errors = {r.get("eglError") or r.get("error") for r in sel} - {None}
        if errors:
            renderer += f"; error: {'; '.join(sorted(errors))}"
        exits = {r.get("exit") for r in sel}
        if exits != {0}:
            renderer += f"; exits {sorted(exits)}"
        out.append(f"| `{method.replace('+', ' ')}` | {kind} | {len(sel)} | {stats([r.get('detectMs') for r in sel])} | "
                   f"{stats([r.get('wallMs') for r in sel])} | {statistics.median(rss):.1f} | {', '.join(verdicts)} | {renderer} |")
    phases = {}
    for r in rows:
        if r["kind"] != "warm":
            continue
        for name, ms in (r.get("phases") or {}).items():
            phases.setdefault((r["method"], name), []).append(ms)
    out += ["", "Warm phase medians (ms):", ""]
    current = None
    line = ""
    for (method, name), values in phases.items():
        if method != current:
            if line:
                out.append(line)
            current = method
            line = f"- `{method.replace('+', ' ')}`:"
        line += f" {name} {statistics.median(values):.2f};"
    if line:
        out.append(line)
    return out


def csp(directory):
    files = sorted(glob.glob(os.path.join(directory, "csp", "*.json")))
    if not files:
        return []
    out = []
    for f in files:
        r = json.load(open(f))
        page = r.get("page") or {}
        out += ["", f"### {os.path.basename(f)}: profile `{r['profile']}`, WebKitGTK {r['webkit']}", ""]
        if page.get("info"):
            out.append(f"origin `{page['info'].get('origin')}`, secure context {page['info'].get('isSecureContext')}, "
                       f"trustedTypes {page['info'].get('trustedTypes')}")
            out.append("")
        out += ["| Probe | Outcome | Violation (blockedURI, effectiveDirective) |", "|---|---|---|"]
        results = list(page.get("page") or [])
        worker = page.get("worker") or {}
        if isinstance(worker, dict):
            results += worker.get("results") or []
            if worker.get("error"):
                out.append(f"| worker | {worker['error']} | |")
        for p in results:
            v = "; ".join(f"`{x.get('blockedURI')}` {x.get('effectiveDirective')}" for x in p.get("violations", [])) or "none"
            out.append(f"| {p['name']} | {p['outcome']} | {v} |")
        conns = {n: len(v["connections"]) for n, v in r["listeners"].items()}
        out.append("")
        out.append("Loopback connections per listener: " + ", ".join(f"{n} {c}" for n, c in conns.items()))
        reqs = [f"{q['method']} {q['uri']}" for q in r["requests"] if not q["uri"].endswith((".js", ".html"))]
        out.append("Scheme requests (other than scripts and the page): " + (", ".join(reqs) or "none"))
        out.append(f"CSP reports received: {len(r['reports'])}")
    return out


def accel(directory):
    files = sorted(glob.glob(os.path.join(directory, "accel", "*.json")))
    if not files:
        return []
    out = ["", "| Run | Env | Settings policy | GSK renderer | Private MB (all processes) | Load finished ms | webkit://gpu excerpt |",
           "|---|---|---|---|---|---|---|"]
    for f in files:
        r = json.load(open(f))
        env = " ".join(f"{k}={v}" for k, v in r["env"].items() if v and k != "GDK_BACKEND") or "-"
        text = (r.get("pageText") or r.get("pageTextError") or "").replace("\n", " / ")
        excerpt = []
        for key in ["Hardware accelerated", "Compositing", "DMA-BUF", "Renderer", "GL_RENDERER", "Platform", "Buffer format", "Disabled"]:
            i = text.find(key)
            if i >= 0:
                excerpt.append(text[i:i + 60])
        out.append(f"| {os.path.basename(f)} | {env} | {r.get('settingsPolicy')} | {r.get('gskRenderer')} | "
                   f"{r.get('privateMbTotal')} | {r.get('loadFinishedMs')} | {' ¦ '.join(excerpt)[:400]} |")
    return out


for d in sys.argv[1:]:
    lines = [f"# {os.path.basename(os.path.normpath(d))}", ""]
    for name in ["series.jsonl", "gdk.jsonl"]:
        p = os.path.join(d, name)
        if os.path.exists(p):
            lines += [f"## {name}", ""] + detection(p) + [""]
    c = csp(d)
    if c:
        lines += ["## connect-src"] + c + [""]
    a = accel(d)
    if a:
        lines += ["## Acceleration default"] + a + [""]
    if os.path.exists(os.path.join(d, "versions.txt")):
        lines += ["## Versions", "", "```", open(os.path.join(d, "versions.txt")).read().strip(), "```", ""]
    text = "\n".join(lines)
    with open(os.path.join(d, "summary.md"), "w") as handle:
        handle.write(text + "\n")
    print(text)
