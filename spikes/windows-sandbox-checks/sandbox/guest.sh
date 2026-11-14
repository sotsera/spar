#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-sandbox-checks/sandbox/guest.sh <script> [-Name value]...
# Stages the published exe and the guest scripts under new names (the guest caches replaced files of a mapped
# folder), waits until the guest sees them, then runs sandbox/<script>.ps1 inside the running sandbox as its
# logged-in user with -Exe <staged exe>. wsb exec relays no output: the script's output goes to
# C:\spar\out\<script>-<time>.log, and results are files under C:\spar\out.
set -eu
id=$(tr -d '\r\n' < artifacts/windows-sandbox-checks/sandbox/id.txt)
script=$1; shift
stage=artifacts/windows-sandbox-checks/sandbox/in
n=$(date +%H%M%S)
cp spikes/windows-sandbox-checks/Checks.Desktop/bin/Release/net11.0/win-x64/publish/Checks.Desktop.exe "$stage/checks-$n.exe"
for f in spikes/windows-sandbox-checks/sandbox/*.ps1; do cp "$f" "$stage/$(basename "$f" .ps1)-$n.ps1"; done
ps1='C:\spar\in\'"$script-$n.ps1"
exe='C:\spar\in\'"checks-$n.exe"
log='C:\spar\out\'"$script-$n.log"
for _ in $(seq 1 40); do
  wsb exec --id "$id" -r ExistingLogin --raw -c "cmd /c if exist $ps1 if exist $exe (exit 0) else (exit 1)" | grep -q '"ExitCode": 0' && break
  sleep 2
done
args=""
while [ $# -gt 0 ]; do args="$args $1 \"$2\""; shift 2; done
wsb exec --id "$id" -r ExistingLogin --raw \
  -c "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Exe $exe$args > $log 2>&1"
