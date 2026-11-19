#!/usr/bin/env bash
# A/B of the fixture's AutoSetFocus change on private copies of the two test outputs (so a concurrent build of the
# repository's own bin folders cannot swap the fixture under the run): artifacts/blackbox-invoke/suites/{nb,wt}-{before,after},
# each the Debug output of samples/Notebook.EndToEndTests or tests/Spar.WindowTests with the Spar.Testing.dll built
# without (before) or with (after) the change. Runs the black-box tests (trait Surface=BlackBox) <count> rounds, each
# round both labels in turn, and prints <label>,<suite>,<round>,<result>,<test>,<ms> per test and a total per suite run.
#   ab.sh <count> [labels] > results/ab.csv
set -u
repo=$(cd "$(dirname "$0")/../.." && pwd)
root=$repo/artifacts/blackbox-invoke/suites
count=$1
labels=${2:-"before after"}
for round in $(seq 1 "$count"); do
  for label in $labels; do
    for suite in nb wt; do
      exe=$( [ "$suite" = nb ] && echo Notebook.EndToEndTests.exe || echo Spar.WindowTests.exe )
      dir=$root/$suite-$label
      rm -f "$dir/run.xml"
      (cd "$dir" && "./$exe" -trait "Surface=BlackBox" -xml run.xml > run.log 2>&1)
      exit=$?
      L="$label" S="$suite" R="$round" perl -ne 'while (/<test [^>]*?name="([^"]+)" result="(\w+)" time="([0-9.]+)"/g) { printf "%s,%s,%s,%s,%s,%.0f\n", $ENV{L}, $ENV{S}, $ENV{R}, $2, $1, $3 * 1000 }' "$dir/run.xml"
      summary=$(grep -E "Total: " "$dir/run.log" | sed -E 's/^ +//')
      echo "$label,$suite,$round,suite,exit $exit,$summary"
    done
  done
done
