#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/startup-prerequisites/sandbox/connect.sh (copied from windows-sandbox-checks)
# Opens a session for the running sandbox (wsb connect) and keeps its client window minimized: the client restores
# itself when the connection completes, so it is minimized again until it stays down.
set -u
id=$(tr -d '\r\n' < artifacts/startup-prerequisites/sandbox/id.txt)
find=spikes/startup-prerequisites/sandbox/find-client.ps1
wsb connect --id "$id" --raw >/dev/null
for i in $(seq 1 40); do
  rows=$(powershell -NoProfile -ExecutionPolicy Bypass -File $find)
  if echo "$rows" | grep -q 'foreground=True'; then powershell -NoProfile -ExecutionPolicy Bypass -File $find -Minimize >/dev/null; echo "minimized at check $i"; fi
  sleep 0.5
done
powershell -NoProfile -ExecutionPolicy Bypass -File $find
