#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-titlebar/sandbox/drive.sh <sandbox id> <mode> "<label args>"...
# Stages the published exe and the guest script under new names (the guest caches replaced files of a mapped
# folder), then runs the configurations inside the running Windows Sandbox as its logged-in user.
set -eu
id=$1; mode=$2; shift 2
stage=artifacts/windows-titlebar/sandbox/in
n=$(date +%H%M%S)
cp spikes/windows-titlebar/Titlebar.Desktop/bin/Release/net11.0/win-x64/publish/Titlebar.Desktop.exe "$stage/titlebar-$n.exe"
cp spikes/windows-titlebar/sandbox/run-guest.ps1 "$stage/run-guest-$n.ps1"
# Newly copied files show up in the guest only after a few seconds.
for _ in $(seq 1 30); do
  wsb exec --id "$id" -r ExistingLogin --raw -c "cmd /c if exist C:\spar\in\run-guest-$n.ps1 if exist C:\spar\in\titlebar-$n.exe (exit 0) else (exit 1)" | grep -q '"ExitCode": 0' && break
  sleep 2
done
configs=""
for c in "$@"; do configs="$configs;$c"; done
configs="\"${configs#;}\""
wsb exec --id "$id" -r ExistingLogin --raw \
  -c "powershell -NoProfile -ExecutionPolicy Bypass -File C:\spar\in\run-guest-$n.ps1 -Exe C:\spar\in\titlebar-$n.exe -Mode $mode -Configs $configs"
