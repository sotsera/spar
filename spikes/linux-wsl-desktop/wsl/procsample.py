#!/usr/bin/env python3
"""Poor man's tracer for the WSLg spike (no strace, perf or gdb in the distribution, and no sudo).

Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop):
    python3 -I wsl/procsample.py <out.jsonl> <interval-ms> -- <command...>

Runs the command and, every interval, reads /proc for each descendant process and thread: state, wchan, the syscall
it is blocked in (/proc/<tid>/syscall, readable for own processes), CPU ticks, and newly mapped files. Writes one JSON
line per change, stamped in ms since the command started, plus the command's own output lines with stamps.
"""
import json
import os
import subprocess
import sys
import threading
import time

out_path = sys.argv[1]
interval = float(sys.argv[2]) / 1000.0
command = sys.argv[sys.argv.index("--") + 1:]
start = time.monotonic()
records = []
lock = threading.Lock()


def stamp():
    return round((time.monotonic() - start) * 1000.0, 1)


def emit(record):
    record["t"] = stamp()
    with lock:
        records.append(record)


def read(path):
    try:
        with open(path, "rb") as f:
            return f.read().decode("utf-8", "replace")
    except OSError:
        return None


def children_of(root):
    parents = {}
    for name in os.listdir("/proc"):
        if name.isdigit():
            stat = read(f"/proc/{name}/stat")
            if stat:
                rest = stat[stat.rfind(")") + 2:].split(" ")
                parents[int(name)] = int(rest[1])
    found, frontier = [], [root]
    while frontier:
        parent = frontier.pop()
        for pid, ppid in parents.items():
            if ppid == parent:
                found.append(pid)
                frontier.append(pid)
    return found


process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)


def pump():
    for line in process.stdout:
        emit({"out": line.rstrip("\n")})


reader = threading.Thread(target=pump, daemon=True)
reader.start()
last_thread = {}
seen_maps = {}
seen_pids = set()
while process.poll() is None:
    pids = [process.pid] + children_of(process.pid)
    for pid in pids:
        comm = (read(f"/proc/{pid}/comm") or "?").strip()
        if pid not in seen_pids:
            seen_pids.add(pid)
            emit({"spawn": pid, "comm": comm, "cmdline": (read(f"/proc/{pid}/cmdline") or "").replace("\0", " ")[:300]})
        maps = read(f"/proc/{pid}/maps")
        if maps:
            files = {line.split(None, 5)[5] for line in maps.splitlines() if len(line.split(None, 5)) == 6 and line.split(None, 5)[5].startswith("/")}
            new = sorted(files - seen_maps.get(pid, set()))
            if new and pid in seen_maps:
                emit({"pid": pid, "comm": comm, "mapped": new})
            seen_maps[pid] = files
        try:
            tids = os.listdir(f"/proc/{pid}/task")
        except OSError:
            continue
        for tid in tids:
            stat = read(f"/proc/{pid}/task/{tid}/stat")
            if not stat:
                continue
            tcomm = stat[stat.find("(") + 1:stat.rfind(")")]
            rest = stat[stat.rfind(")") + 2:].split(" ")
            state, utime, stime = rest[0], int(rest[11]), int(rest[12])
            wchan = (read(f"/proc/{pid}/task/{tid}/wchan") or "").strip()
            syscall = (read(f"/proc/{pid}/task/{tid}/syscall") or "").split(" ")[0].strip()
            key = (pid, tid)
            now = (state, wchan, syscall)
            previous = last_thread.get(key)
            if previous is None or previous[0] != now:
                emit({"pid": pid, "comm": comm, "tid": int(tid), "thread": tcomm, "state": state, "wchan": wchan, "syscall": syscall, "cpu": utime + stime})
            last_thread[key] = (now, utime + stime)
    time.sleep(interval)

reader.join(timeout=2)
with open(out_path, "w") as f:
    for record in records:
        f.write(json.dumps(record) + "\n")
print(f"procsample: {len(records)} records, exit {process.returncode}")
