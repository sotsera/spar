#!/usr/bin/env bash
# Usage: bash wsl/series.sh (inside WSL, after sync.sh and publish.sh). Every part-2 measurement, in order; logs in
# $base/logs. RUNS (default 60) sets the long series.
set -u
here=$(cd "$(dirname "$0")" && pwd)
source "$here/common.sh"
runs=${RUNS:-60}
r() { bash "$here/run.sh" "$@" >/dev/null; echo "$1: $(grep -E '^RESULT|^RUN exit' "$base/logs/$1.log" | tr '\n' ' ')"; }
r j-aot-xtest 97 aot journey --runs "$runs" --screenshots
r j-aot-script 97 aot journey --runs "$runs" --input script --screenshots
r j-jit-xtest 97 jit journey --runs 20
r j-aot-insert 97 aot journey --runs 20 --fill insert
r j-aot-csp 97 aot journey --runs 10 --csp
# Two test processes at once, each with its own Xvfb (XTEST's pointer and focus are per display).
bash "$here/run.sh" j-par-1 97 aot journey --runs 15 >/dev/null &
p1=$!
bash "$here/run.sh" j-par-2 98 aot journey --runs 15 >/dev/null &
p2=$!
wait $p1 $p2
echo "parallel: $(grep -h -E '^RESULT' "$base/logs/j-par-1.log" "$base/logs/j-par-2.log" | tr '\n' ' ')"
r act-xtest 97 aot actions
r act-script 97 aot actions --input script
r cov-xtest 97 aot coverage
r cov-script 97 aot coverage --input script
r cov-xtest-csp 97 aot coverage --csp
r activation 97 aot activation
r iso-1 97 aot isolation
r iso-2 97 aot isolation
bash "$here/sandbox-paths.sh"
