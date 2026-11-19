#!/usr/bin/env bash
# Copies the evidence of Windows Sandbox runs from artifacts/test-results/Spar.SandboxTests/<run>/ into
# docs/evidence/m1b-sandbox/runs/<run>/: the summary, each session's timeline and configuration, and each job's guest
# notes, exit code and the tests' own notes. The repository's path becomes <repo>; user profile paths and SIDs are
# redacted. Usage: collect.sh <run> [<run> ...]
set -eu
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
repo_win=$(cygpath -w "$repo")
for run in "$@"; do
  from="$repo/artifacts/test-results/Spar.SandboxTests/$run"
  to="$repo/docs/evidence/m1b-sandbox/runs/$run"
  rm -rf "$to"; mkdir -p "$to"
  cp "$from/summary.txt" "$to/"
  for session in "$from"/s[0-9]*; do
    name=$(basename "$session")
    mkdir -p "$to/$name"
    cp "$session/out/timeline.jsonl" "$session/out/config.xml" "$to/$name/"
    for job in "$session"/out/j*; do
      [ -d "$job" ] || continue
      jn=$(basename "$job"); mkdir -p "$to/$name/$jn"
      for f in guest.txt exitcode.txt error.log; do [ -f "$job/$f" ] && cp "$job/$f" "$to/$name/$jn/"; done
      find "$job/test-results" -name 'checks.txt' -o -name '*.uia.txt' -o -name '*.png' 2>/dev/null | while read -r file; do
        rel="${file#$job/test-results/}"; mkdir -p "$to/$name/$jn/$(dirname "$rel")"; cp "$file" "$to/$name/$jn/$rel"
      done
      # The JSON reporter's stream, reduced to the test messages that carry an outcome.
      grep -E '"\$type":"test-(starting|passed|failed|skipped|not-run)"' "$job/console.log" > "$to/$name/$jn/tests.jsonl" 2>/dev/null || true
    done
  done
  find "$to" -type f \( -name '*.txt' -o -name '*.jsonl' -o -name '*.xml' -o -name '*.log' \) -print0 \
    | xargs -0 env REPO="$repo_win" perl "$repo/docs/evidence/m1b-sandbox/redact.pl"
done
