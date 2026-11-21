"""Per-condition medians and ranges of an A/B log of wsl.sh (its LAUNCH lines), the warm-up round left out.

python3 -I summary.py <ab log>...
"""
import re
import statistics
import sys

PHASES = ["gtk_ready", "window_shown", "page_committed", "first_bridge_message", "ui-ready"]


def stats(values):
    values = sorted(values)
    return f"{statistics.median(values):6.1f} ({values[0]:.1f}–{values[-1]:.1f})"


for path in sys.argv[1:]:
    rows = {}
    portal = {}
    with open(path, encoding="utf-8") as log:
        for line in log:
            if not line.startswith("LAUNCH "):
                continue
            fields = dict(part.split("=", 1) for part in line.split() if "=" in part)
            if fields["round"] == "0":
                continue
            values = {name: float(fields[name]) for name in PHASES if name in fields}
            if "ui-ready" not in values or fields["host_started"] == "?":
                print(f"{path}: launch {fields['n']} ({fields['condition']}) has no ui-ready line")
                continue
            values["total"] = float(fields["host_started"]) + values["ui-ready"]
            values["after_window"] = values["ui-ready"] - values["window_shown"]
            values["before"] = float(fields["a11y_before_ms"])
            rows.setdefault(fields["condition"], []).append(values)
            portal.setdefault(fields["condition"], []).append("xdg-desktop-por" in fields.get("processes", ""))
    print(f"== {path}")
    columns = ["total", *PHASES, "after_window", "before"]
    print("cond  n  " + "  ".join(f"{name:>20}" for name in columns) + "  portal running")
    for condition in sorted(rows):
        values = rows[condition]
        cells = "  ".join(f"{stats([row[name] for row in values]):>20}" for name in columns)
        print(f"{condition:>4} {len(values):>2}  {cells}  {sum(portal[condition])}/{len(values)}")
