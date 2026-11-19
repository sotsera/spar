#!/usr/bin/env bash
# The SYSTEM-only part of the sign-out spike: no wsb connect, so no client window reaches the host's desktop.
# Starts a sandbox, restarts the guest (shutdown /r /f), then shuts it down (shutdown /s /f), and records what wsb and
# the mapped output folder report after each (uptime from the guest's tick count). Usage: system-restart.sh <name>
set -u
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/results/$1"
mkdir -p "$out"
host_out="$(cd "$here/../.." && pwd)/artifacts/sandbox-signout/$1"
rm -rf "$host_out"; mkdir -p "$host_out"
win_out=$(cygpath -w "$host_out")
log="$out/timeline.txt"; : > "$log"
now() { date +%s%3N; }
t0=$(now)
note() { local ms=$(( $(now) - t0 )); printf '%6d.%03d %s\n' $((ms/1000)) $((ms%1000)) "$*" >> "$log"; }
flat() { tr -d '\r' | tr '\n' ' ' | tr -s ' '; }
uptime_cmd='powershell -NoProfile -Command "[Environment]::TickCount64"'
note "list-before $(wsb list --raw | flat)"
id=$(powershell -NoProfile -Command "[guid]::NewGuid().ToString()" | tr -d '\r')
config="<Configuration><Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection><PrinterRedirection>Disable</PrinterRedirection><VideoInput>Disable</VideoInput><AudioInput>Disable</AudioInput><MappedFolders><MappedFolder><HostFolder>$win_out</HostFolder><SandboxFolder>C:\spar\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder></MappedFolders></Configuration>"
note "start $(wsb start --id "$id" --config "$config" --raw 2>&1 | flat)"
for i in $(seq 1 60); do r=$(wsb exec --id "$id" -r System -c "cmd /c exit 0" --raw 2>&1 | flat); if echo "$r" | grep -q '"ExitCode": 0'; then break; fi; done
note "ready after $i probes: $r"
wsb exec --id "$id" -r System -c "cmd /c > C:\spar\out\before.txt $uptime_cmd" --raw >/dev/null 2>&1
before=$(flat < "$host_out/before.txt")
note "uptime-before-ms: $before"
note "restart-exec $(wsb exec --id "$id" -r System -c 'shutdown /r /f /t 0' --raw 2>&1 | flat)"
for i in $(seq 1 90); do
  l=$(wsb list --raw 2>&1 | flat)
  rm -f "$host_out/after.txt"
  r=$(wsb exec --id "$id" -r System -c "cmd /c > C:\spar\out\after.txt $uptime_cmd" --raw 2>&1 | flat)
  u=$(flat < "$host_out/after.txt" 2>/dev/null)
  note "after-restart probe $i listed=$(echo "$l" | grep -c "$id") exec=$r uptime-ms=$u"
  if ! echo "$l" | grep -q "$id"; then break; fi
  # A restart shows as an uptime below the one measured before it.
  if [ -n "$u" ] && [ "${u%% *}" -lt "${before%% *}" ] 2>/dev/null; then break; fi
done
note "files-after-restart: $(ls "$host_out" | flat)"
note "shutdown-exec $(wsb exec --id "$id" -r System -c 'shutdown /s /f /t 0' --raw 2>&1 | flat)"
for i in $(seq 1 60); do
  l=$(wsb list --raw 2>&1 | flat)
  r=$(wsb exec --id "$id" -r System -c "cmd /c exit 0" --raw 2>&1 | flat)
  note "after-shutdown probe $i listed=$(echo "$l" | grep -c "$id") exec=$r"
  if ! echo "$l" | grep -q "$id"; then break; fi
done
note "stop $(wsb stop --id "$id" --raw 2>&1 | flat)"
note "list-after $(wsb list --raw | flat)"
sed -i "s/$id/<id>/g" "$log"
cat "$log"
