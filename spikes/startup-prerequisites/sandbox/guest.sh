#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/startup-prerequisites/sandbox/guest.sh <script> [-Name value]...
# Adapted from spikes/windows-sandbox-checks/sandbox/guest.sh. Stages both Startup.Win variants, the probe and the
# guest scripts under new names (the guest caches replaced files of a mapped folder), waits until the guest sees
# them, then runs sandbox/<script>.ps1 inside the sandbox as its logged-in user with -Bin <staged folder>. wsb exec
# relays no output: the script's output goes to C:\spar\out\<script>-<time>.log.
set -eu
id=$(tr -d '\r\n' < artifacts/startup-prerequisites/sandbox/id.txt)
script=$1; shift
n=$(date +%H%M%S)
stage=artifacts/startup-prerequisites/sandbox/in/$n
mkdir -p "$stage"
spike=spikes/startup-prerequisites
cp $spike/Startup.Win/bin/Release/net11.0/win-x64/publish/Startup.Win.exe "$stage/app.exe"
cp $spike/Startup.Win/bin/nomanifest/Release/net11.0/win-x64/publish/Startup.Win.exe "$stage/app-nomanifest.exe"
cp $spike/Startup.Probe/bin/Release/net11.0/win-x64/publish/Startup.Probe.exe "$stage/probe.exe"
cp $spike/sandbox/*.ps1 "$stage/"
bs='\'
bin="C:${bs}spar${bs}in${bs}$n"
ps1="$bin$bs$script.ps1"
probe="$bin${bs}probe.exe"
log="C:${bs}spar${bs}out${bs}$script-$n.log"
for _ in $(seq 1 40); do
  wsb exec --id "$id" -r ExistingLogin --raw -c "cmd /c if exist $ps1 if exist $probe (exit 0) else (exit 1)" | grep -q '"ExitCode": 0' && break
  sleep 2
done
args=""
while [ $# -gt 0 ]; do args="$args $1 \"$2\""; shift 2; done
wsb exec --id "$id" -r ExistingLogin --raw \
  -c "cmd /c powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Bin $bin$args > $log 2>&1"
echo "log: artifacts/startup-prerequisites/sandbox/out/$script-$n.log"
