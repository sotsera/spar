#!/usr/bin/env python3
"""Runs GpuProbe methods as separate processes and records one JSON line per run (probe output plus wall time).

Usage: python3 -I series.py <probe> <out.jsonl> <label> <rounds> <cold> <method>...
  method: the probe's arguments joined with '+', e.g. 'egl+--platform+surfaceless' or 'combined'.
  cold:   number of cold runs per method first; before each, the page cache of every shared library and of the
          probe is dropped with posix_fadvise(DONTNEED) (pages mapped by running processes stay).
Then <rounds> warm rounds, the methods interleaved within each round.
"""
import json
import os
import subprocess
import sys
import time

LIB_DIRS = ["/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib"]


def evict(paths):
    dropped = 0
    seen = set()
    for root_dir in paths:
        for root, dirs, files in os.walk(root_dir):
            # The WSL driver share (/usr/lib/wsl, mounted in the dxg containers) is slow to walk and is not on the
            # default path.
            dirs[:] = [d for d in dirs if os.path.join(root, d) != "/usr/lib/wsl"]
            for name in files:
                p = os.path.join(root, name)
                if ".so" not in name and not os.access(p, os.X_OK):
                    continue
                try:
                    st = os.stat(p)
                    key = (st.st_dev, st.st_ino)
                    if key in seen or not os.path.isfile(p):
                        continue
                    seen.add(key)
                    fd = os.open(p, os.O_RDONLY)
                    try:
                        os.posix_fadvise(fd, 0, 0, os.POSIX_FADV_DONTNEED)
                        dropped += 1
                    finally:
                        os.close(fd)
                except OSError:
                    pass
    return dropped


def run(probe, method, kind, label, out):
    argv = [probe] + method.split("+")
    t0 = time.perf_counter_ns()
    proc = subprocess.run(argv, capture_output=True, text=True)
    wall = (time.perf_counter_ns() - t0) / 1e6
    line = proc.stdout.strip().splitlines()[-1] if proc.stdout.strip() else "{}"
    try:
        rec = json.loads(line)
    except json.JSONDecodeError:
        rec = {"raw": proc.stdout[-500:]}
    rec.update({"label": label, "method": method, "kind": kind, "wallMs": round(wall, 3), "exit": proc.returncode,
                "stderrTail": proc.stderr[-300:]})
    out.write(json.dumps(rec) + "\n")
    out.flush()
    return rec


def main():
    probe, out_path, label, rounds, cold = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5])
    methods = sys.argv[6:]
    dirs = LIB_DIRS + [os.path.dirname(os.path.abspath(probe))]
    with open(out_path, "a") as out:
        for m in methods:
            for _ in range(cold):
                n = evict(dirs)
                rec = run(probe, m, "cold", label, out)
                print(f"cold {m}: detect={rec.get('detectMs')} wall={rec['wallMs']} verdict={rec.get('verdict')} (evicted {n})")
        for i in range(rounds):
            for m in methods:
                rec = run(probe, m, "warm", label, out)
                if i == 0:
                    print(f"warm {m}: detect={rec.get('detectMs')} wall={rec['wallMs']} verdict={rec.get('verdict')} "
                          f"renderer={rec.get('glRenderer')} reason={rec.get('reason')}")


if __name__ == "__main__":
    main()
