#!/usr/bin/env python3
"""Which libraries is a blocked thread inside? (WSLg spike; no gdb, strace or perf, and no sudo.)

Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop):
    python3 -I wsl/stackpeek.py <thread-name> <trigger-text> <delay-ms> -- <command...>

Runs the command; <delay-ms> after a line containing <trigger-text> appears in its output, reads the named thread's
/proc/<pid>/task/<tid>/syscall (syscall number, arguments, stack pointer, program counter), then reads 256 KB of its
stack from /proc/<pid>/mem (allowed because this script is the parent) and lists, in stack order, the words that
point into executable mappings: library name and offset, the closest thing to a backtrace available here. Each
library is reported once per contiguous run of frames.
"""
import os
import subprocess
import sys
import threading
import time

thread_name, trigger, delay = sys.argv[1], sys.argv[2], float(sys.argv[3]) / 1000.0
command = sys.argv[sys.argv.index("--") + 1:]
process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
start = time.monotonic()
hit = threading.Event()


def pump():
    for line in process.stdout:
        print(f"{(time.monotonic() - start) * 1000:7.1f} | {line.rstrip()}")
        if trigger in line:
            hit.set()


threading.Thread(target=pump, daemon=True).start()
hit.wait(20)
time.sleep(delay)
pid = process.pid
tid = next(t for t in os.listdir(f"/proc/{pid}/task") if open(f"/proc/{pid}/task/{t}/comm").read().strip() == thread_name)
fields = open(f"/proc/{pid}/task/{tid}/syscall").read().split()
print(f"PEEK at {(time.monotonic() - start) * 1000:.1f} ms thread {thread_name} tid {tid}: syscall {fields}")
maps = []
for line in open(f"/proc/{pid}/maps"):
    parts = line.split(None, 5)
    low, high = (int(x, 16) for x in parts[0].split("-"))
    path = parts[5].strip() if len(parts) == 6 else ""
    maps.append((low, high, parts[1], int(parts[2], 16), path))
sp = int(fields[-2], 16)
pc = int(fields[-1], 16)


def where(address):
    for low, high, perms, offset, path in maps:
        if low <= address < high:
            return perms, path, address - low + offset
    return None


print("pc:", where(pc))
with open(f"/proc/{pid}/mem", "rb") as mem:
    mem.seek(sp)
    data = mem.read(256 * 1024)
last = None
count = 0
for i in range(0, len(data) - 7, 8):
    word = int.from_bytes(data[i:i + 8], "little")
    found = where(word)
    if found and "x" in found[0] and found[1]:
        name = found[1].split("/")[-1]
        if name != last:
            print(f"  sp+{i:#07x}  {name} +{found[2]:#x}")
            last = name
            count += 1
        if count > 60:
            break
process.wait()
