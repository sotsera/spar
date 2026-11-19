#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-single-instance/sandbox/run.sh start|connect|phases|stop
#   start    writes the configuration (no network, no camera, no microphone; folders mapped from artifacts/) and starts
#            a sandbox with a new id, saved to artifacts/windows-single-instance/sandbox/id.txt
#   connect  opens a session (wsb connect) and keeps the client window minimized, with the helper from
#            spikes/windows-sandbox-checks
#   phases   stages the probe and the guest script under new names, then runs the SYSTEM and user phases
#   stop     stops the sandbox
set -eu
base=artifacts/windows-single-instance/sandbox
win() { cygpath -aw "$1"; }

case "$1" in
  start)
    mkdir -p "$base/in" "$base/out"
    config="<Configuration><Networking>Disable</Networking><VideoInput>Disable</VideoInput><AudioInput>Disable</AudioInput>\
<ClipboardRedirection>Disable</ClipboardRedirection><PrinterRedirection>Disable</PrinterRedirection>\
<MappedFolders>\
<MappedFolder><HostFolder>$(win "$base/in")</HostFolder><SandboxFolder>C:\spar\in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>\
<MappedFolder><HostFolder>$(win "$base/out")</HostFolder><SandboxFolder>C:\spar\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>\
</MappedFolders></Configuration>"
    printf '%s\n' "$config" > "$base/config.xml"
    id=$(powershell -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r')
    printf '%s\n' "$id" > "$base/id.txt"
    wsb start --id "$id" --config "$config" --raw
    ;;
  connect)
    id=$(tr -d '\r\n' < "$base/id.txt")
    find=spikes/windows-sandbox-checks/sandbox/find-client.ps1
    wsb connect --id "$id" --raw >/dev/null
    for i in $(seq 1 40); do
      rows=$(powershell -NoProfile -ExecutionPolicy Bypass -File $find)
      if echo "$rows" | grep -q 'foreground=True'; then powershell -NoProfile -ExecutionPolicy Bypass -File $find -Minimize >/dev/null; echo "minimized at check $i"; fi
      sleep 0.5
    done
    ;;
  phases)
    id=$(tr -d '\r\n' < "$base/id.txt")
    n=$(date +%H%M%S)
    cp spikes/windows-single-instance/Single.Win/bin/Release/net11.0/win-x64/publish/Single.Win.exe "$base/in/si-$n.exe"
    cp spikes/windows-single-instance/sandbox/guest.ps1 "$base/in/guest-$n.ps1"
    exe='C:\spar\in\'"si-$n.exe"
    ps1='C:\spar\in\'"guest-$n.ps1"
    for _ in $(seq 1 40); do
      wsb exec --id "$id" -r ExistingLogin --raw -c "cmd /c if exist $ps1 if exist $exe (exit 0) else (exit 1)" | grep -q '"ExitCode": 0' && break
      sleep 2
    done
    # user <phase> [security] [name]; system <probe arguments> <log name>
    user() { wsb exec --id "$id" -r ExistingLogin --raw -c "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Exe $exe -Phase $1 -Security ${2:-spar} -Name \"${3:-}\" > C:\\spar\\out\\$1-${3:-}-$n.log 2>&1"; }
    system() { wsb exec --id "$id" -r System --raw -c "cmd /c $exe $1 > C:\\spar\\out\\system-$2-$n.log 2>&1"; }

    user prepare
    for security in dotnet spar; do
      # A SYSTEM process holds the pipe name before the user's app starts, owned by Administrators.
      system "squat --data C:\\SparData\\squat-$security --log C:\\spar\\out\\squat-$security\\system --owner BA --hold-ms 9000 --accept" "squat-$security" &
      sleep 3
      user squatted $security "squat-$security"
      wait
      # A SYSTEM process tries to connect to the user's running primary.
      user probe-target $security "probe-$security" &
      sleep 3
      system "probe --data C:\\SparData\\probe-$security --log C:\\spar\\out\\probe-$security\\system" "probe-$security"
      wait
    done
    if [ "${2:-}" = "focus" ]; then user focus; fi
    ;;
  stop)
    wsb stop --id "$(tr -d '\r\n' < "$base/id.txt")"
    ;;
esac
