#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/page-heartbeat/sandbox/connect.sh
# (Copied from spikes/windows-sandbox-checks.) Opens a session for the running sandbox (wsb connect; exec needs one)
# and keeps its client window minimized: the client restores itself when the connection completes.
set -u
id=$(tr -d '\r\n' < artifacts/page-heartbeat/sandbox/id.txt)
find=spikes/page-heartbeat/sandbox/find-client.ps1
wsb connect --id "$id" --raw >/dev/null
for i in $(seq 1 40); do
  rows=$(powershell -NoProfile -ExecutionPolicy Bypass -File $find)
  if echo "$rows" | grep -q 'foreground=True'; then powershell -NoProfile -ExecutionPolicy Bypass -File $find -Minimize >/dev/null; echo "minimized at check $i"; fi
  sleep 0.5
done
powershell -NoProfile -ExecutionPolicy Bypass -File $find
