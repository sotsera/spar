#!/usr/bin/env bash
# The full window suite (tests/Spar.WindowTests, CoreCLR Debug, every test that is not explicit) run N times back to
# back, each run's console kept under artifacts/renderer-sharing-runs/ (disposable) and one line per run appended to
# results/suite-runs.txt: the summary, the failed tests, and the install-path and session-end lines that run wrote.
# Git Bash, from the repository root, after `dotnet build tests/Spar.WindowTests -c Debug`.
# Usage: bash spikes/renderer-sharing/scripts/suite-runs.sh <runs> <label>; a label starting with "dotnet-test" runs
# `dotnet test --project tests/Spar.WindowTests -c Debug --no-build` instead of the executable.
set -u
runs=${1:-30}
label=${2:-suite}
exe=tests/Spar.WindowTests/bin/Development/Debug/net11.0/win-x64/Spar.WindowTests.exe
logs=artifacts/renderer-sharing-runs/$label
out=spikes/renderer-sharing/results/suite-runs.txt
results=artifacts/test-results/Spar.WindowTests
mkdir -p "$logs"
echo "== $label: $runs runs, started $(date -u +%FT%TZ)" >> "$out"
for run in $(seq 1 "$runs"); do
  before_install=$(wc -l < "$results/install-path.txt" 2>/dev/null || echo 0)
  before_session=$(wc -l < "$results/session-end.txt" 2>/dev/null || echo 0)
  log="$logs/run-$(printf %02d "$run").log"
  started=$(date +%s)
  if [[ $label == dotnet-test* ]]; then
    # As a developer runs it: through dotnet test (Microsoft.Testing.Platform), on the build already there.
    dotnet test --project tests/Spar.WindowTests -c Debug --no-build > "$log" 2>&1
  else
    "$exe" -noColor > "$log" 2>&1
  fi
  code=$?
  took=$(( $(date +%s) - started ))
  summary=$(grep -E "Total: [0-9]+|total: [0-9]+|^ *(failed|succeeded|skipped): [0-9]+" "$log" | sed 's/^ *//' | tr '\n' ' ')
  failed=$(grep -E "\[FAIL\]|^ *failed [A-Za-z]" "$log" | sed 's/^ *//; s/\[FAIL\] //' | tr '\n' ';')
  install=$(tail -n +"$((before_install + 1))" "$results/install-path.txt" 2>/dev/null | cut -c30- | tr '\n' ';')
  session=$(tail -n +"$((before_session + 1))" "$results/session-end.txt" 2>/dev/null | cut -c30- | grep -E "held|renderer" | sed -E 's/its window.s renderer\(s\) [0-9, ]+ ended/renderer ended/' | tr '\n' ';')
  echo "run $run ($label): exit $code in ${took} s | $summary | failed: ${failed:-none} | install: $install | session end: $session" >> "$out"
done
echo "== $label: finished $(date -u +%FT%TZ)" >> "$out"
