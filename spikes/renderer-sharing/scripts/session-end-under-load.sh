#!/usr/bin/env bash
# The renderer test's session end under load: the test run N times, each in a new test process, while two other copies
# of the window suite run beside it (other test processes, their own windows and browsers). The load leaves
# SessionEndHostTests out, so each new renderer line is this run's; it goes to results/session-end-under-load.txt. A
# failure's message (with the host's log lines after "session ending") is kept in the run's console under
# artifacts/renderer-sharing-runs/under-load/.
# Git Bash, from the repository root, after `dotnet build tests/Spar.WindowTests -c Debug`.
set -u
runs=${1:-10}
exe=tests/Spar.WindowTests/bin/Development/Debug/net11.0/win-x64/Spar.WindowTests.exe
logs=artifacts/renderer-sharing-runs/under-load
out=spikes/renderer-sharing/results/session-end-under-load.txt
results=artifacts/test-results/Spar.WindowTests
mkdir -p "$logs"
echo "== $runs runs with two full suites as load, started $(date -u +%FT%TZ)" >> "$out"
"$exe" -noColor -class- Spar.WindowTests.SessionEndHostTests > "$logs/load-a.log" 2>&1 &
load_a=$!
"$exe" -noColor -class- Spar.WindowTests.SessionEndHostTests > "$logs/load-b.log" 2>&1 &
load_b=$!
for run in $(seq 1 "$runs"); do
  before=$(wc -l < "$results/session-end.txt")
  log="$logs/run-$(printf %02d "$run").log"
  "$exe" -noColor -method "Spar.WindowTests.SessionEndHostTests.A_renderer_lost_during_the_session_end_is_neither_reloaded_nor_waited_for" > "$log" 2>&1
  code=$?
  line=$(tail -n +"$((before + 1))" "$results/session-end.txt" | grep "renderer test" | cut -c30- | sed -E 's/its window.s renderer\(s\) [0-9, ]+ ended/renderer ended/')
  load="load running: $(kill -0 $load_a 2>/dev/null && echo a)$(kill -0 $load_b 2>/dev/null && echo b)"
  echo "run $run: exit $code | $line | $load" >> "$out"
  if ! kill -0 $load_a 2>/dev/null; then "$exe" -noColor -class- Spar.WindowTests.SessionEndHostTests >> "$logs/load-a.log" 2>&1 & load_a=$!; fi
  if ! kill -0 $load_b 2>/dev/null; then "$exe" -noColor -class- Spar.WindowTests.SessionEndHostTests >> "$logs/load-b.log" 2>&1 & load_b=$!; fi
done
wait $load_a $load_b
echo "== finished $(date -u +%FT%TZ)" >> "$out"
