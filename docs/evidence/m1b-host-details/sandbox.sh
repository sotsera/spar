#!/usr/bin/env bash
# The Windows Sandbox part of docs/evidence/m1b-host-details.md (Git Bash, from the repository root). One sandbox
# without network, camera, microphone, clipboard or printers; mapped folders under artifacts/m1b-host-details/sandbox/
# (in: read-only, out: results). The guest grants the foreground to every process and takes real input through
# SendInput; nothing is sent to the host's desktop. The client window that `wsb connect` opens is minimized as soon as
# it appears (spikes/windows-sandbox-checks/sandbox/find-client.ps1), and closed at the end.
#   docs/evidence/m1b-host-details/sandbox.sh start|connect|focus <exe> <label>|leak <label>|stop
set -eu
base=artifacts/m1b-host-details/sandbox
here=docs/evidence/m1b-host-details/sandbox
find=spikes/windows-sandbox-checks/sandbox/find-client.ps1
win() { cygpath -aw "$1"; }
id() { tr -d '\r\n' < "$base/id.txt"; }
# Guest folders, kept in variables: a backslash before $ in a double-quoted string would escape the $.
gin='C:\spar\in\'
gout='C:\spar\out\'
gwork='C:\spar-work\'

# Copies files into the read-only folder under new names (the guest caches replaced files) and waits until it sees them.
stage() {
  local n=$1
  shift
  for f in "$@"; do cp "$f" "$base/in/$n-$(basename "$f")"; done
  for _ in $(seq 1 40); do
    wsb exec --id "$(id)" -r ExistingLogin --raw -c "cmd /c if exist $gin$n-$(basename "$1") (exit 0) else (exit 1)" | grep -q '"ExitCode": 0' && return 0
    sleep 2
  done
  echo "staging $n timed out" >&2
  return 1
}

case "$1" in
  start)
    mkdir -p "$base/in" "$base/out"
    config="<Configuration><Networking>Disable</Networking><VideoInput>Disable</VideoInput><AudioInput>Disable</AudioInput>\
<vGPU>Enable</vGPU><ClipboardRedirection>Disable</ClipboardRedirection><PrinterRedirection>Disable</PrinterRedirection>\
<MappedFolders>\
<MappedFolder><HostFolder>$(win "$base/in")</HostFolder><SandboxFolder>C:\spar\in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>\
<MappedFolder><HostFolder>$(win "$base/out")</HostFolder><SandboxFolder>C:\spar\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>\
</MappedFolders></Configuration>"
    printf '%s\n' "$config" > "$base/config.xml"
    powershell -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r' > "$base/id.txt"
    wsb start --id "$(id)" --config "$config" --raw
    ;;
  connect)
    # wsb connect returns only when the session ends (pitfall "Windows Sandbox"), so it runs in the background.
    wsb connect --id "$(id)" --raw >/dev/null &
    for i in $(seq 1 40); do
      rows=$(powershell -NoProfile -ExecutionPolicy Bypass -File $find)
      if echo "$rows" | grep -q 'foreground=True'; then powershell -NoProfile -ExecutionPolicy Bypass -File $find -Minimize >/dev/null; echo "minimized at check $i"; fi
      sleep 0.5
    done
    powershell -NoProfile -ExecutionPolicy Bypass -File $find
    ;;
  focus)
    exe=$2 label=$3 n=$(date +%H%M%S)
    stage "$n" "$exe" "$here/focus.ps1" "$here/Guest.cs"
    wsb exec --id "$(id)" -r ExistingLogin --raw -c "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File $gin$n-focus.ps1 -Exe $gin$n-$(basename "$exe") -Label $label -Helper $gin$n-Guest.cs > $gout$label-$n.log 2>&1"
    ;;
  leak)
    label=$2 n=$(date +%H%M%S)
    stage "$n" artifacts/m1b-host-details/leakprobe/LeakProbe.exe artifacts/m1b-host-details/leakprobe/WebView2Loader.dll "$here/leak.ps1"
    # The loader must sit beside the executable under its own name.
    probe="${gwork}probe-$n"
    wsb exec --id "$(id)" -r ExistingLogin --raw -c "cmd /c mkdir $probe & copy $gin$n-LeakProbe.exe $probe\\LeakProbe.exe & copy $gin$n-WebView2Loader.dll $probe\\WebView2Loader.dll"
    wsb exec --id "$(id)" -r ExistingLogin --raw -c "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File $gin$n-leak.ps1 -Exe $probe\\LeakProbe.exe -Label $label > $gout$label-$n.log 2>&1"
    ;;
  stop)
    for h in $(powershell -NoProfile -ExecutionPolicy Bypass -File $find | grep -o '^[0-9]*'); do
      powershell -NoProfile -ExecutionPolicy Bypass -File spikes/windows-sandbox-checks/sandbox/close-client.ps1 -Hwnd "$h" || true
    done
    wsb stop --id "$(id)" --raw || true
    ;;
esac
