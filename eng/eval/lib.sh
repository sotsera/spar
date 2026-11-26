#!/usr/bin/env bash
# Helpers for the tasks' seed, reference and grade scripts (eng/eval/README.md). Each script runs from the task's
# solution folder, with EVAL_TASK set to the task's folder in eng/eval/tasks.
set -euo pipefail

# replace FILE OLD NEW: replaces the one literal occurrence of OLD in FILE with NEW, and fails if OLD is not there.
replace() {
    local file=$1
    OLD=$2 NEW=$3 perl -0pi -e 'my $old = $ENV{OLD}; my $new = $ENV{NEW}; my $count = s/\Q$old\E/$new/; die "not found\n" unless $count' "$file" \
        || { echo "replace: the text was not found in $file" >&2; return 1; }
}

# hidden: copies the task's hidden files (its hidden/ folder) into the solution, where only the graders see them.
hidden() {
    cp -r "$EVAL_TASK/hidden/." .
}

# check NAME COMMAND...: runs one check and prints its result as a line the runner reads; never stops the script.
check() {
    local name=$1
    shift
    if "$@" > "/tmp/check-$name.log" 2>&1; then
        echo "CHECK $name pass"
    else
        echo "CHECK $name fail"
        tail -n 40 "/tmp/check-$name.log" | sed 's/^/  /'
    fi
}

# fails: succeeds when the command fails (for mutation checks).
fails() {
    ! "$@"
}

# The checks' own test runs: Debug, quiet, one at a time.
test_class() {
    local project=$1 class=$2
    dotnet test --project "$project" -c Debug --filter-class "$class" --minimum-expected-tests 1
}

# The helpers, for checks that run in a bash -c of their own.
export -f replace fails test_class
