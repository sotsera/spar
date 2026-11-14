#!/usr/bin/env bash
# Usage (in the container, in /work/spikes/theme-startup, as uid 1000 with DISPLAY set):
#   linux/run.sh <gtk3|gtk4> <label> [app args...]
# Starts the X11 capture (linux/xcap) and then the app; stops the capture when the app has exited. Output goes to
# artifacts/theme-startup/<label>/ (timeline.json from the app, frames.jsonl and PNGs from xcap, run.log).
set -u
variant=$1; label=$2; shift 2
dir=Theme.Linux/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Theme.Linux/bin/gtk4/Release/net11.0/linux-x64/publish
out=../../artifacts/theme-startup/$label
rm -rf "$out"; mkdir -p "$out"
# The window sits at 0,0 (no window manager). With a header bar the title band is at the top.
regions="0 0 960 720"
case " $* " in *" --headerbar "*) regions="0 60 960 650 120 6 600 28" ;; esac
# Under Xvfb only WebKit's first frames reach the X window with the DMA-BUF renderer off, and none with it on (see
# RESULTS.md), so the run turns it off: the capture then shows everything up to the page's first paint.
export WEBKIT_DISABLE_DMABUF_RENDERER=${WEBKIT_DISABLE_DMABUF_RENDERER-1}
linux/xcap "$out" 20000 $regions 2>>"$out/run.log" &
cap=$!
sleep 0.2
"$dir/Theme.Linux" --label "$label" "$@" >>"$out/run.log" 2>&1
code=$?
kill -TERM $cap 2>/dev/null; wait $cap
left=$(pgrep -f 'WebKit(Web|Network|GPU)Process' | wc -l)
echo "SPAR_RUN $variant $label exit=$code webkit-processes-left=$left" | tee -a "$out/run.log"
