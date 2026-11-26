#!/usr/bin/env bash
# M2a's devtools smoke on Windows (docs/evidence/m2a-devtools.md), from Git Bash at the repository's root, after
# dotnet build Spar.slnx -c Debug: the Notebook's Debug apphost on a desktop of the run's own (CreateDesktop, through
# ../m1d-hot-reload/WatchDesktop.cs, so no window reaches the monitor, takes the focus or gets input), its data folders
# under artifacts/webview2-profiles/m2a-devtools/<run>/, then smoke.sh against it with the built spar tool. The log, with
# the repository's and the user's paths replaced, goes to docs/evidence/m2a-devtools/logs/windows-smoke.txt.
set -u
repo=$(pwd -W)
run=$(date -u +%Y%m%d-%H%M%S)
root="$repo/artifacts/webview2-profiles/m2a-devtools/$run"
mkdir -p artifacts/m2a-devtools docs/evidence/m2a-devtools/logs

dotnet run docs/evidence/m1d-hot-reload/WatchDesktop.cs -- --project samples/Notebook --root "$root" \
  --log "$repo/artifacts/m2a-devtools/windows-$run.run.log" --steps "until:ui-ready;sleep:90000" \
  --exe "$repo/samples/Notebook/bin/Development/Debug/net11.0/win-x64/Notebook.exe" --cwd "$repo/samples/Notebook" \
  > "artifacts/m2a-devtools/windows-$run.harness.txt" 2>&1 &
harness=$!

SMOKE_SHOT="$repo/docs/evidence/m2a-devtools/windows-notebook.png" \
  bash docs/evidence/m2a-devtools/smoke.sh samples/Notebook "$repo/src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.exe" \
  > "artifacts/m2a-devtools/windows-$run.smoke.txt" 2>&1
echo "smoke done; the harness ends the app at its 90 s"
wait $harness

sed -e "s#$(printf '%s' "$repo" | sed 's#[\\/]#[\\\\/]#g')#<repo>#g" -e 's#C:[\\/]Users[\\/][^\\/]*#<USER>#g' \
  "artifacts/m2a-devtools/windows-$run.smoke.txt" > docs/evidence/m2a-devtools/logs/windows-smoke.txt
echo "wrote docs/evidence/m2a-devtools/logs/windows-smoke.txt"
