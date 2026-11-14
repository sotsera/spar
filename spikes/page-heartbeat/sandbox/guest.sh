#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/page-heartbeat/sandbox/guest.sh <group> [runs]
# Stages the sandbox build of the exe, the guest script and the group's configuration list under new names (the guest
# caches replaced files of a mapped folder), waits until the guest sees them, then runs sandbox/run.ps1 inside the
# running sandbox as its logged-in user. wsb exec relays no output: logs land in
# artifacts/page-heartbeat/sandbox/out/<group>/ (C:\spar\out\<group>\ in the guest).
set -eu
id=$(tr -d '\r\n' < artifacts/page-heartbeat/sandbox/id.txt)
group=$1; runs=${2:-3}
stage=artifacts/page-heartbeat/sandbox/in
n=$(date +%H%M%S)
cp spikes/page-heartbeat/Beat.Win/bin/publish-sandbox/Beat.Win.exe "$stage/beat-$n.exe"
cp spikes/page-heartbeat/sandbox/run.ps1 "$stage/run-$n.ps1"
bash spikes/page-heartbeat/sandbox/cases.sh "$group" "$runs" > "$stage/configs-$n.txt"
ps1='C:\spar\in\'"run-$n.ps1"
exe='C:\spar\in\'"beat-$n.exe"
cfg='C:\spar\in\'"configs-$n.txt"
log='C:\spar\out\'"run-$group-$n.log"
for _ in $(seq 1 40); do
  wsb exec --id "$id" -r ExistingLogin --raw -c "cmd /c if exist $ps1 if exist $exe if exist $cfg (exit 0) else (exit 1)" | grep -q '"ExitCode": 0' && break
  sleep 2
done
wsb exec --id "$id" -r ExistingLogin --raw \
  -c "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Exe $exe -Configs $cfg -Out $group > $log 2>&1"
