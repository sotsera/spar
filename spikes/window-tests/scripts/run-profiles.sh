#!/usr/bin/env bash
# Question 1, in-process modes, then two processes at once, then the crash variants. Git Bash, repository root.
set -u
exe=spikes/window-tests/WindowTests.Win/bin/publish/WindowTests.Win.exe
out=artifacts/window-tests
mkdir -p "$out"
count=${COUNT:-25}
for mode in inprivate profile-delete profile-clear udf; do
  "$exe" profiles --mode "$mode" --count "$count" --memory --root "solo-$mode" > "$out/solo-$mode.log" 2>&1
  grep -E '^(SUMMARY|SHARED-END|MEM)' "$out/solo-$mode.log"
done
for mode in inprivate udf; do
  "$exe" parallel --mode "$mode" --count "$count" --label par > "$out/parallel-$mode.log" 2>&1
  cat "$out/parallel-$mode.log"
done
for variant in host host-reputation tree; do
  "$exe" crash --variant "$variant" --runs 5 > "$out/crash-$variant.log" 2>&1
  cat "$out/crash-$variant.log"
done
"$exe" crash --variant host-reputation --runs 3 --eager > "$out/crash-host-reputation-eager.log" 2>&1
cat "$out/crash-host-reputation-eager.log"
