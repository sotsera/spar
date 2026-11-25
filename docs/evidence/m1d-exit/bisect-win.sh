#!/usr/bin/env bash
# M1d's exit, Windows: the M1a browser and command budgets at earlier commits, to find when they grew
# (docs/evidence/m1d-exit.md, "The M1a lines"). Each commit is extracted with `git archive` (no worktree, no change to
# the repository's git state) into %TEMP%\spar-m1d-exit-bisect\<commit>, its TypeScript SDK installed and built, and
# WireShapes' BudgetTests run in Release, RUNS times (2 by default). From Git Bash at the repository's root:
#   bash docs/evidence/m1d-exit/bisect-win.sh <commit>...
# Results: artifacts/m1d-exit/bisect/<commit>-<run>-{command,browser}.txt. The copies are deleted after each commit.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
out=$repo/artifacts/m1d-exit/bisect
base=$(cygpath -u "$TEMP")/spar-m1d-exit-bisect
mkdir -p "$out" "$base"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
unset CI SPAR_TEST_RESULTS
for commit in "$@"; do
  dir=$base/$commit
  rm -rf "$dir" && mkdir -p "$dir"
  (cd "$repo" && git archive "$commit") | tar -x -C "$dir"
  (cd "$dir/src/Spar.Client" && npm ci --ignore-scripts --no-audit --no-fund >/dev/null && npm run build >/dev/null) || { echo "$commit: SDK build failed"; continue; }
  for run in $(seq 1 "${RUNS:-2}"); do
    (cd "$dir" && dotnet test --project tests/WireShapes.Tests -c Release -- --filter-class "WireShapes.Tests.BudgetTests" >"$out/$commit-$run-command.log" 2>&1)
    cp "$dir/artifacts/test-results/WireShapes.Tests/budgets.txt" "$out/$commit-$run-command.txt" 2>/dev/null
    (cd "$dir" && dotnet test --project tests/WireShapes.EndToEndTests -c Release -- --filter-class "WireShapes.EndToEndTests.BudgetTests" >"$out/$commit-$run-browser.log" 2>&1)
    cp "$dir/artifacts/test-results/WireShapes.EndToEndTests/budgets.txt" "$out/$commit-$run-browser.txt" 2>/dev/null
    echo "$commit run $run:"; grep -h "budget" "$out/$commit-$run-command.txt" "$out/$commit-$run-browser.txt"
  done
  rm -rf "$dir"
done
