"""Summarizes the memory runs (run with python3 -I).

Usage: python3 -I wsl/summarize.py RUNS_DIR OUT_DIR

Reads RUNS_DIR/*/mem.jsonl, groups runs by configuration (label without the trailing -rN), and writes:
  memory-totals.txt     total and per-role PSS and USS (MB), median (min-max) over rounds, per point
  memory-classes.txt    per role: anonymous, file-backed and shared-memory/GPU PSS and USS (medians)
  memory-families.txt   per role: PSS by library family (medians), for the ready and heavy points
  memory-top.txt        the 15 largest mappings by PSS per role, from the last round, ready and heavy
"""

import glob
import json
import os
import re
import statistics
import sys

runs_dir, out_dir = sys.argv[1], sys.argv[2]
POINTS = ("ready", "idle30", "heavy")


def role_group(role):
    if role.startswith("sandbox-helper"):
        return "sandbox"
    if role.startswith("electron-utility"):
        return "electron-utility"
    return role


def mb(kb):
    return kb / 1024


def fmt(values):
    if not values:
        return "-"
    med = statistics.median(values)
    if len(values) == 1:
        return f"{med:.0f}"
    return f"{med:.0f} ({min(values):.0f}-{max(values):.0f})"


records = {}
excluded = []
for path in sorted(glob.glob(os.path.join(runs_dir, "*", "mem.jsonl"))):
    with open(path) as handle:
        for line in handle:
            rec = json.loads(line)
            # A snapshot taken while a process outside the run mapped the same WebKit/GTK/Mesa libraries is not used
            # (the run's own portal, started in its private session, is not foreign). Records without the field
            # predate the check and are trusted only if the caller kept them in RUNS_DIR.
            others = [f for f in rec.get("foreign", []) if not f.startswith("xdg-desktop-por")]
            if others:
                excluded.append(f"{rec['label']} {rec['point']}: {' '.join(others)}")
                continue
            config = re.sub(r"-r\d+$", "", rec["label"])
            records.setdefault(config, {}).setdefault(rec["point"], []).append(rec)


def by_role(rec):
    roles = {}
    for p in rec["processes"]:
        r = roles.setdefault(role_group(p["role"]), {"pss": 0, "uss": 0, "n": 0, "classes": {}, "families": {}})
        r["pss"] += p["rollup"]["Pss"]
        r["uss"] += p["uss"]
        r["n"] += 1
        for c, v in p["classes"].items():
            cc = r["classes"].setdefault(c, {"pss": 0, "uss": 0})
            cc["pss"] += v["pss"]
            cc["uss"] += v["uss"]
        for f, v in p["families"].items():
            ff = r["families"].setdefault(f, {"pss": 0, "uss": 0})
            ff["pss"] += v["pss"]
            ff["uss"] += v["uss"]
    return roles


ROLE_ORDER = ["ui", "web", "network", "sandbox", "gpu", "electron-main", "electron-zygote", "electron-gpu-process",
              "electron-renderer", "electron-utility", "electron-crashpad"]


def ordered(roles):
    return [r for r in ROLE_ORDER if r in roles] + sorted(r for r in roles if r not in ROLE_ORDER)


with open(os.path.join(out_dir, "memory-totals.txt"), "w") as out:
    out.write("Total and per-role PSS / USS in MB: median (min-max) over rounds. USS = Private_Clean + Private_Dirty.\n")
    out.write("Roles: ui = app process (GTK + WebKit UI side + .NET), web = WebKitWebProcess, network = WebKitNetworkProcess,\n")
    out.write("sandbox = bwrap + xdg-dbus-proxy; electron-* = Electron's main, zygotes, GPU, renderer and utility processes.\n\n")
    for config in sorted(records):
        for point in POINTS:
            recs = records[config].get(point)
            if not recs:
                continue
            totals_pss = [mb(sum(p["rollup"]["Pss"] for p in r["processes"])) for r in recs]
            totals_uss = [mb(sum(p["uss"] for p in r["processes"])) for r in recs]
            roles = [by_role(r) for r in recs]
            names = ordered({k for r in roles for k in r})
            parts = []
            for name in names:
                pss = [mb(r[name]["pss"]) for r in roles if name in r]
                uss = [mb(r[name]["uss"]) for r in roles if name in r]
                parts.append(f"{name} {fmt(pss)} / {fmt(uss)}")
            out.write(f"{config:34s} {point:7s} n={len(recs)}  TOTAL PSS {fmt(totals_pss)} USS {fmt(totals_uss)}\n")
            out.write(f"{'':34s} {'':7s}      " + "; ".join(parts) + "\n")
        out.write("\n")

with open(os.path.join(out_dir, "memory-classes.txt"), "w") as out:
    out.write("Per role, medians in MB: PSS / USS of anonymous (heap, stacks, JS and GC heaps, llvmpipe buffers), file-backed\n")
    out.write("(code, data, fonts, caches; mostly clean and reclaimable) and shared-memory/GPU mappings (memfd, /dev/shm, SysV, DRM).\n\n")
    for config in sorted(records):
        for point in POINTS:
            recs = records[config].get(point)
            if not recs:
                continue
            roles = [by_role(r) for r in recs]
            names = ordered({k for r in roles for k in r})
            line = []
            for name in names:
                if name == "sandbox":
                    continue
                cells = []
                for c in ("anon", "file", "shm"):
                    pss = statistics.median(mb(r[name]["classes"].get(c, {"pss": 0})["pss"]) for r in roles if name in r)
                    uss = statistics.median(mb(r[name]["classes"].get(c, {"uss": 0})["uss"]) for r in roles if name in r)
                    cells.append(f"{c} {pss:.0f}/{uss:.0f}")
                line.append(f"{name}: " + " ".join(cells))
            out.write(f"{config:34s} {point:7s} " + " | ".join(line) + "\n")
        out.write("\n")

with open(os.path.join(out_dir, "memory-families.txt"), "w") as out:
    out.write("Per role, median PSS in MB by library family (file-backed unless named anon or shared-memory-gpu).\n")
    out.write("Families under 1 MB are omitted.\n\n")
    for config in sorted(records):
        for point in ("ready", "heavy"):
            recs = records[config].get(point)
            if not recs:
                continue
            roles = [by_role(r) for r in recs]
            for name in ordered({k for r in roles for k in r}):
                if name == "sandbox":
                    continue
                fams = {f for r in roles if name in r for f in r[name]["families"]}
                cells = []
                for f in fams:
                    v = statistics.median(mb(r[name]["families"].get(f, {"pss": 0})["pss"]) for r in roles if name in r)
                    if v >= 1:
                        cells.append((v, f))
                cells.sort(reverse=True)
                out.write(f"{config:34s} {point:6s} {name:20s} " + ", ".join(f"{f} {v:.0f}" for v, f in cells) + "\n")
        out.write("\n")

with open(os.path.join(out_dir, "memory-top.txt"), "w") as out:
    out.write("Largest mappings by PSS (KB) per process, from the last round. Columns: PSS, USS, RSS, class, name.\n\n")
    for config in sorted(records):
        for point in ("ready", "heavy"):
            recs = records[config].get(point)
            if not recs:
                continue
            rec = recs[-1]
            out.write(f"== {rec['label']} {point}\n")
            for p in rec["processes"]:
                if role_group(p["role"]) == "sandbox" or p["rollup"]["Pss"] < 2048:
                    continue
                out.write(f"  -- {p['role']} ({p['comm']}) PSS {p['rollup']['Pss']} KB, USS {p['uss']} KB\n")
                for g in p["top"][:15]:
                    out.write(f"     {g['pss']:8d} {g['uss']:8d} {g['rss']:8d} {g['class']:4s} {g['name']}\n")
                big = [a for a in p["top_anon"] if a["pss"] > 4096]
                if big:
                    out.write("     largest anonymous mappings (size KB / PSS KB): "
                              + ", ".join(f"{a['size']}/{a['pss']} {a['perms']}{(' ' + a['path']) if a['path'] else ''}" for a in big) + "\n")
            out.write("\n")
with open(os.path.join(out_dir, "memory-excluded.txt"), "w") as out:
    out.write("Snapshots excluded because a process outside the run mapped WebKit, GTK or Mesa libraries:\n")
    out.write("\n".join(excluded) + "\n")
print("excluded", len(excluded), "snapshots")
print("summarized", sum(len(v) for c in records.values() for v in c.values()), "snapshots in", len(records), "configurations")
