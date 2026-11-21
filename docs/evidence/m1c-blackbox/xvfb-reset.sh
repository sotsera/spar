#!/usr/bin/env bash
# Xvfb on an abstract socket as the black box starts it, with and without -noreset; short-lived clients one after another.
for mode in reset noreset; do
  extra=(); [ $mode = noreset ] && extra=(-noreset)
  Xvfb :199 -screen 0 1280x800x24 -nolisten tcp -nolisten unix "${extra[@]}" 2>/dev/null & pid=$!
  sleep 1
  python3 xvfb-reset.py :199 3000 "$mode (one process)"
  ok=0; bad=0
  for i in $(seq 1 300); do python3 -c 'import ctypes,sys; x=ctypes.CDLL("libX11.so.6"); x.XOpenDisplay.restype=ctypes.c_void_p; sys.exit(0 if x.XOpenDisplay(b":199") else 1)' && ok=$((ok+1)) || bad=$((bad+1)); done
  echo "$mode (one process per open): 300 processes, $bad failed"
  kill $pid; wait $pid 2>/dev/null
done
