#!/usr/bin/env bash
# Usage: killparent.sh <app path> [off] [nostdin]   SIGKILLs the launcher and reports whether the app survived.
# A zombie (state Z) has exited: PID 1 of a plain container ("sleep infinity") does not reap orphans.
cd "$(dirname "$0")"
dotnet build killparent.cs -o /tmp/killparent >/dev/null || exit 1
/tmp/killparent/killparent "$1" "${2:-on}" "${3:-stdin}" > /tmp/kp.out &
for _ in $(seq 1 100); do grep -q app= /tmp/kp.out && break; sleep 0.05; done
read -r p a k < /tmp/kp.out; parent=${p#parent=}; app=${a#app=}
sleep 0.5; kill -9 "$parent" 2>/dev/null; s=$(date +%s%N)
state=""; for _ in $(seq 1 100); do state=$(awk '{print $3}' /proc/$app/stat 2>/dev/null); [ -z "$state" ] || [ "$state" = Z ] && break; sleep 0.01; done
echo "$k: app $app state after parent SIGKILL: ${state:-gone} after $(( ($(date +%s%N)-s)/1000000 )) ms (Z or gone = exited)"
[ -n "$state" ] && [ "$state" != Z ] && kill -9 "$app"
true
