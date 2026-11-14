# Usage (inside WSL): python3 -I wsl/summarize-titlebar.py <runs folder> <label prefix>
# One line per title-bar run: what each step produced, from the host log (M lines, STEP lines, XT lines) and the root
# watcher (xwatch.log). Times are CLOCK_MONOTONIC ms, shared by the host, xtool and the script.
import json, os, re, sys

root, prefix = sys.argv[1], sys.argv[2]
for label in sorted(os.listdir(root)):
    if not label.startswith(prefix):
        continue
    log = open(os.path.join(root, label, "host.log")).read().splitlines()
    watch_path = os.path.join(root, label, "xwatch.log")
    watch = open(watch_path).read().splitlines() if os.path.exists(watch_path) else []
    steps, current = {}, None
    for line in log:
        if line.startswith("STEP "):
            current = line.split(" ", 2)[2]
            steps[current] = [float(line.split()[1]), []]
        elif current:
            steps[current][1].append(line)
    out = {}
    def find(step, pattern):
        for k, (t, lines) in steps.items():
            if k.startswith(step):
                return t, [l for l in lines if re.search(pattern, l)]
        return None, []
    t, l = find("click the search", r"\"type\":\"focus\"|begin-move")
    out["search"] = "focus,no-move" if any("focus" in x for x in l) and not any("begin-move" in x for x in l) else "FAIL " + " | ".join(l)[:80]
    t, l = find("drag the bar", r"begin-move|XT .* (before|after|press at)")
    bm = [x for x in l if "begin-move" in x]
    before = [x for x in l if " before " in x]; after = [x for x in l if " after " in x]
    pos = lambda s: re.search(r"root=(-?\d+,-?\d+)", s).group(1) if s else "?"
    press = [float(x.split()[1]) for x in l if "press at" in x]
    mr = [x for x in watch if "_NET_WM_MOVERESIZE" in x]
    first_mr = float(mr[0].split()[1]) if mr else None
    out["drag"] = (re.search(r"press->call=([\d.]+)ms", bm[0]).group(1) + "ms press->begin_move" if bm else "no begin-move") + \
        (f"; WM request {first_mr - press[0]:.1f}ms after XTEST press data={mr[0].split('data=')[1]}" if mr and press else "; no WM request") + \
        f"; moved={'yes' if before and after and pos(before[0]) != pos(after[0]) else 'no'} ({pos(before[0] if before else '')} -> {pos(after[0] if after else '')})"
    for name, step in [("dbl1", "double-click the bar"), ("dbl2", "double-click the bar again")]:
        t, l = find(step, r"toggle-maximize|state-seen|begin-move|press [(]")
        if name == "dbl1":
            t, l = [(tt, ll) for k, (tt, ll) in steps.items() if k == "double-click the bar"][0]
            l = [x for x in l if re.search(r"toggle-maximize|state-seen|begin-move|press \(", x)]
        seen = [x for x in l if "state-seen" in x]
        presses = [float(x.split()[1]) for x in l if "press (" in x]
        out[name] = (f"state={json.loads(seen[0].split('page ',1)[1])['state']} {float(seen[0].split()[1]) - presses[-1]:.1f}ms after 2nd press" if seen and presses else "no state change") + \
            ("; begin-move!" if any("begin-move" in x for x in l) else "")
    t, l = find("right-click", r"window-menu")
    out["menu"] = l[0].split("window-menu ")[1][:7] if l else "none"
    for name, step in [("wm-add", "maximize from outside"), ("wm-remove", "restore from outside")]:
        t, l = find(step, r"state-seen")
        out[name] = f"{json.loads(l[0].split('page ',1)[1])['state']} {float(l[0].split()[1]) - t:.1f}ms" if l else "not seen"
    t, l = find("click the page's maximize", r"state-seen")
    out["max-button"] = json.loads(l[0].split('page ',1)[1])['state'] if l else "not seen"
    t, l = find("click the page's minimize", r"WINDOWS-SIDE|window-state|facts:")
    out["minimize"] = "; ".join(re.sub(r".*(iconic=\w+).*", r"windows \1", x) if "WINDOWS" in x else re.sub(r".*(state=[\w+]+).*", r"gtk \1", x) for x in l)
    t, l = find("unminimize", r"WINDOWS-SIDE")
    out["present"] = re.sub(r".*(iconic=\w+).*", r"windows \1", l[0]) if l else "?"
    t, l = find("page-side resize", r"begin-resize|facts:|XT .* (before|after)")
    if t:
        br = [x for x in l if "begin-resize" in x]
        sizes = [re.search(r"size=(\d+x\d+)", x).group(1) for x in l if " before " in x or " after " in x]
        out["edge-resize"] = (br[0].split("begin-resize ")[1][:40] if br else "no begin-resize") + f" X size {' -> '.join(sizes)}" + \
            ("; WM request " + [x for x in watch if "MOVERESIZE" in x and not re.search(r'data=[^,]+,[^,]+,(8|11),', x)][-1].split("data=")[1] if br else "")
    t, l = find("close through", r"quit|children|exit")
    end = [x for x in log if x.startswith("left=") or "host exit" in x]
    out["close"] = ("quit" if any("quit" in x for x in l) else "no quit") + "; " + ("children-exited" if any("children-exited" in x for x in l) else "children?")
    print(label)
    for k, v in out.items():
        print(f"  {k:12} {v}")
