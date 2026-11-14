#!/usr/bin/env bash
# Publishes each size variant (NativeAOT, win-x64, Release), runs the checks, and runs the cross-process credential
# check. Run from anywhere; paths are relative to this folder. Output: artifacts/results/.
set -euo pipefail
cd "$(dirname "$0")"

project=NativeServices.App
results=artifacts/results
mkdir -p "$results"

declare -A variants=(
  [none]="None"
  [dialogs]="Dialogs"
  [com-baseline]="ComBaseline"
  [com-baseline-dialogs]="ComBaseline;Dialogs"
  [com-baseline-cswin32-dialogs]="ComBaseline;CsWin32Dialogs"
  [cswin32-dialogs]="CsWin32Dialogs"
  [dialogs-grants]="Dialogs;Grants"
  [credman]="CredMan"
  [dpapi]="Dpapi"
  [protecteddata]="ProtectedData"
  [all]="Dialogs;CsWin32Dialogs;Grants;CredMan;Dpapi;ProtectedData"
)

: > "$results/sizes.txt"
for name in none com-baseline dialogs com-baseline-dialogs cswin32-dialogs com-baseline-cswin32-dialogs dialogs-grants \
  credman dpapi protecteddata all; do
  dir="$project/bin/variants/$name"
  dotnet publish "$project" -c Release -p:SpikeParts="${variants[$name]//;/%3B}" -o "$dir" > "$results/publish-$name.log" 2>&1
  warnings=$(grep -c -E ': warning [A-Z]+[0-9]+' "$results/publish-$name.log" || true)
  size=$(stat -c %s "$dir/$project.exe")
  echo "$name parts=${variants[$name]} exe=$size warnings=$warnings" | tee -a "$results/sizes.txt"
done

# Every variant runs its own checks (the dialog ones are dismissed automatically).
for name in none com-baseline dialogs cswin32-dialogs dialogs-grants credman dpapi protecteddata; do
  "$project/bin/variants/$name/$project.exe" all > "$results/run-$name.txt" 2>&1 || echo "run $name failed"
  tail -n 1 "$results/run-$name.txt"
done

all="$project/bin/variants/all/$project.exe"
"$all" all > "$results/run-all.txt" 2>&1 || echo "run all failed"
tail -n 1 "$results/run-all.txt"

# Process A writes a SESSION credential; cmdkey (another process) lists it; process B reads and deletes it.
"$all" cred-write-peer > "$results/peer.txt" 2>&1
cmdkey "/list:SparSpike/native-services/*" >> "$results/peer.txt" 2>&1 || true
"$all" cred-read-peer >> "$results/peer.txt" 2>&1
"$all" cred-verify >> "$results/peer.txt" 2>&1
cat "$results/peer.txt"
