#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-memory, after env.sh):
#   bash wsl/run.sh <gtk3|gtk4|floor-gtk3|floor-gtk4|electron> <label> <points> [host args...]
# points: comma list of ready, idle30, heavy. Starts the app with a control FIFO, waits for its ready mark, then takes a
# smaps snapshot of the whole process tree at each point (ready + 2 s; 30 s after that; 3 s after the heavy page
# reported its rows), asks the app to quit and checks that nothing is left. Output: $ART/runs/<label>/.
set -u
source wsl/env.sh
variant=$1; label=$2; points=$3; shift 3
ART=${ART:-$(cd ../.. && pwd)/artifacts/linux-memory}
OUT=$ART/runs/$label; rm -rf "$OUT"; mkdir -p "$OUT"
LOG=$OUT/host.log; CTL=$OUT/control.fifo; MEM=$OUT/mem.jsonl
mkfifo "$CTL"
echo "# label=$label variant=$variant backend=${GDK_BACKEND:-wayland} env: WEBKIT_DISABLE_DMABUF_RENDERER=${WEBKIT_DISABLE_DMABUF_RENDERER:-} GSK_RENDERER=${GSK_RENDERER:-} args=$*" >"$LOG"
case $variant in
  gtk3|gtk4) timeout 180 "$(bash wsl/exe.sh "$variant")" --label "$label" --control "$CTL" "$@" >>"$LOG" 2>&1 & ;;
  floor-gtk3|floor-gtk4) timeout 180 "wsl/$variant" 120 "Spar mem $label" >>"$LOG" 2>&1 & ;;
  electron) timeout 180 bash electron/start.sh "$label" "$CTL" "$@" >>"$LOG" 2>&1 & ;;
esac
TPID=$!
send() { timeout 2 bash -c 'echo "$1" >"$2"' _ "$1" "$CTL" 2>/dev/null || echo "SEND FAILED: $1" >>"$LOG"; }
wait_mark() {
  local pattern=$1 timeout=${2:-30000} waited=0
  while ! grep -qE "$pattern" "$LOG" 2>/dev/null; do
    sleep 0.05; waited=$((waited + 50))
    if [ $waited -ge "$timeout" ]; then echo "TIMEOUT /$pattern/ $label" | tee -a "$LOG"; return 1; fi
  done
}
app_pid() {  # the app itself: the child of timeout (for electron, the electron main process under start.sh)
  local p; p=$(pgrep -P "$TPID" | head -1)
  if [ "$variant" = electron ]; then p=$(pgrep -P "$p" -x electron | head -1); fi
  echo "$p"
}
snap() {
  local point=$1 pid; pid=$(app_pid)
  case $variant in gtk*|electron) send gc; sleep 0.3;; esac
  echo "SNAP $(date +%s.%N) $point pid=$pid" >>"$LOG"
  python3 -I wsl/mem.py "$pid" "$MEM" "$label" "$point" | tee -a "$OUT/summary.txt"
}
if wait_mark "SPAR_READY" 40000; then
  sleep 2
  case ",$points," in *,ready,*) snap ready;; esac
  case ",$points," in *,idle30,*) sleep 30; snap idle30;; esac
  case ",$points," in *,heavy,*) send "load heavy"; if wait_mark "SPAR_HEAVY" 40000; then sleep 3; snap heavy; fi;; esac
fi
case $variant in
  floor-*) kill "$(app_pid)" 2>/dev/null;;
  *) send quit;;
esac
for _ in $(seq 1 400); do kill -0 "$TPID" 2>/dev/null || break; sleep 0.05; done
if kill -0 "$TPID" 2>/dev/null; then echo "STILL RUNNING after 20 s, stopping our own app" | tee -a "$LOG"; kill "$TPID"; fi
wait "$TPID" 2>/dev/null; echo "exit=$?" >>"$LOG"
sleep 0.3
left=$(ps -u "$(id -u)" -o pid=,stat=,comm= | awk '$2 !~ /^Z/ && $3 ~ /^(WebKit|Mem.Linux|bwrap|xdg-dbus-proxy|floor-gtk|electron|chrome_crashpad)/ {print $1":"$3}' | tr '\n' ' ')
echo "left=[$left]" | tee -a "$LOG"
