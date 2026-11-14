#!/usr/bin/env bash
# Shared helpers for the scenario scripts (sourced, inside WSL, from the working copy's spikes/linux-integration).
# start_host VARIANT LABEL [host args...]   starts the host in the background with a control FIFO; LOG, CTL, HOST_PID
# send "command"                            writes one command to the control FIFO
# wait_mark REGEX [timeout ms]              waits for a matching line in LOG (prints it)
# stop_host                                 sends quit, waits up to 10 s, kills our own host only if it is still there
# rect NAME                                 "x y" of a page element (window-relative, X11) from ui-ready + facts
# left_processes                            our host's and WebKit's processes still alive (zombies excluded)
ART=$(mkdir -p ../../artifacts/linux-integration && cd ../../artifacts/linux-integration && pwd)
export XDG_CACHE_HOME=$ART/cache
mkdir -p "$XDG_CACHE_HOME"

mono() { python3 -I -c 'import time; print(f"{time.monotonic()*1000:.3f}")'; }

start_host() {
  local variant=$1; LABEL=$2; shift 2
  OUT=$ART/runs/$LABEL; rm -rf "$OUT"; mkdir -p "$OUT"
  LOG=$OUT/host.log; CTL=$OUT/control.fifo; TITLE="Spar integ $LABEL"
  mkfifo "$CTL"
  echo "# backend=${GDK_BACKEND:-wayland} variant=$variant args=$*" >"$LOG"
  timeout 120 "$(bash wsl/exe.sh "$variant")" --label "$LABEL" --control "$CTL" "$@" >>"$LOG" 2>&1 &
  HOST_PID=$!
}

# Writes time out, so a host that died never blocks the script.
send() { timeout 2 bash -c 'echo "$1" >"$2"' _ "$1" "$CTL" || echo "SEND FAILED: $1"; }

wait_mark() {
  local pattern=$1 timeout=${2:-10000} waited=0
  while ! grep -qE "$pattern" "$LOG" 2>/dev/null; do
    sleep 0.02; waited=$((waited + 20))
    if [ $waited -ge "$timeout" ]; then echo "TIMEOUT waiting for /$pattern/ in $LABEL"; return 1; fi
  done
  grep -E "$pattern" "$LOG" | tail -1
}

count_marks() { grep -cE "$1" "$LOG"; }

stop_host() {
  kill -0 "$HOST_PID" 2>/dev/null && send quit
  for _ in $(seq 1 500); do kill -0 "$HOST_PID" 2>/dev/null || break; sleep 0.02; done
  if kill -0 "$HOST_PID" 2>/dev/null; then echo "HOST STILL RUNNING, killing pid $HOST_PID"; kill "$HOST_PID"; fi
  wait "$HOST_PID" 2>/dev/null; echo "host exit=$?"
  sleep 0.3
  echo "left=[$(left_processes)]"
}

left_processes() {
  ps -u "$(id -u)" -o pid=,stat=,comm= | awk '$2 !~ /^Z/ && $3 ~ /^(WebKit|Integ.Linux|bwrap|xdg-dbus-proxy)/ {print $1, $3}' | tr '\n' ' '
}

# Page element centre in window coordinates: the view's offset in the X window plus the element's CSS centre (dpr 1).
rect() {
  python3 -I - "$LOG" "$1" <<'PY'
import json, re, sys
log, name = sys.argv[1], sys.argv[2]
ready = facts = None
for line in open(log):
    if " ui-ready " in line: ready = json.loads(line.split(" ui-ready ", 1)[1])
    if " facts:" in line: facts = line
ox, oy = map(int, re.search(r"view-in-surface=(-?\d+),(-?\d+)", facts).groups())
x, y = ready["rects"][name][:2]
print(ox + x, oy + y)
PY
}

# Windows-side rectangle (and with a second argument, an image) of the WSLg window; Windows input is never used.
wincap() {
  local repo; repo=$(cat wsl/winrepo)
  local out=""; [ -n "${2:-}" ] && out=$(wslpath -w "$2")
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$repo\spikes\linux-integration\win\wincap.ps1" -Title "$1" ${out:+-Out "$out"} | tr -d '\r'
}
