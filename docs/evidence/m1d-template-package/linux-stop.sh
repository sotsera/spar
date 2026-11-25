#!/usr/bin/env bash
# Lists the processes the guide run in WSL left (guide-linux.sh): those whose environment has its runtime folder; with
# `kill`, ends them (SIGTERM, then SIGKILL 2 s later).
mine=()
for p in /proc/[0-9]*; do
  pid=${p#/proc/}
  [ "$pid" = $$ ] && continue
  grep -qa "XDG_RUNTIME_DIR=/dev/shm/spar-guide-m1d6a" "$p/environ" 2>/dev/null || continue
  [ "$(cat /proc/$pid/comm 2>/dev/null)" = bash ] && grep -qa linux-stop "$p/cmdline" && continue
  mine+=("$pid"); echo "$pid $(tr '\0' ' ' < $p/cmdline | cut -c1-90)"
done
[ "${1:-}" = kill ] && [ ${#mine[@]} -gt 0 ] && kill -TERM "${mine[@]}" 2>/dev/null && sleep 2 && kill -KILL "${mine[@]}" 2>/dev/null
echo "listed ${#mine[@]}"
