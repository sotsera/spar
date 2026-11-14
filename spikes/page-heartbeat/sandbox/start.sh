#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/page-heartbeat/sandbox/start.sh
# (Adapted from spikes/windows-sandbox-checks.) Writes the Windows Sandbox configuration (no network, no video or audio
# input, no clipboard or printer; mapped folders under artifacts/) and starts a sandbox with a new id, saved to
# artifacts/page-heartbeat/sandbox/id.txt.
set -eu
base=artifacts/page-heartbeat/sandbox
mkdir -p "$base/in" "$base/out" artifacts/webview2-profiles/page-heartbeat-sandbox
win() { cygpath -aw "$1"; }
config="<Configuration><Networking>Disable</Networking><VideoInput>Disable</VideoInput><AudioInput>Disable</AudioInput>\
<vGPU>Enable</vGPU><ClipboardRedirection>Disable</ClipboardRedirection><PrinterRedirection>Disable</PrinterRedirection>\
<MappedFolders>\
<MappedFolder><HostFolder>$(win "$base/in")</HostFolder><SandboxFolder>C:\spar\in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>\
<MappedFolder><HostFolder>$(win "$base/out")</HostFolder><SandboxFolder>C:\spar\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>\
<MappedFolder><HostFolder>$(win artifacts/webview2-profiles/page-heartbeat-sandbox)</HostFolder><SandboxFolder>C:\spar\profiles</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>\
</MappedFolders></Configuration>"
printf '%s\n' "$config" > "$base/config.xml"
id=$(powershell -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')
printf '%s\n' "$id" > "$base/id.txt"
wsb start --id "$id" --config "$config" --raw
