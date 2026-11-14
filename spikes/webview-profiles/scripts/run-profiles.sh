#!/usr/bin/env bash
# Q3-Q5 on Windows: several windows and profiles, memory, clearing data, deleting profiles and folders, test profiles.
set -u
source "$(dirname "$0")/common.sh"
host() { "$exe" host --reputation off "$@"; }
clean() { for u in "$@"; do "$exe" harness clean --udf "$u" > /dev/null; done; }

out="$results/profiles-isolation.txt"; : > "$out"
clean q3-multi q3-priv
echo "== multi: default, default, second, InPrivate" >> "$out"
host --scenario multi --windows "q3-multi/default,q3-multi/default,q3-multi/second,q3-multi/~private" --exit wait \
  | grep -E "^(PROFILE|RESULT|SET-PERMISSION|PERMISSIONS|TOKEN-SCAN|WRITTEN-SINCE|BROWSER-EXITED|CONTROLLER) " | cut -c1-900 >> "$out"
echo "== after exit: InPrivate token on disk" >> "$out"
"$exe" host --scenario scan --udf q3-multi --token PRIVx7q9 | grep TOKEN-SCAN >> "$out"
for spec in "q3-priv/default,q3-priv/~private,q3-priv/~private" "q3-priv/second,q3-priv/~private:second,q3-priv/~private:second"; do
  echo "== InPrivate lifetime: $spec" >> "$out"
  host --scenario private --windows "$spec" --exit wait | grep -E "^(PROFILE|RESULT) " | cut -c1-600 >> "$out"
done
echo "== profile names" >> "$out"
clean q3-names
host --scenario names --udf q3-names --exit wait | grep -E "^NAME " >> "$out"

out="$results/profiles-memory.txt"; : > "$out"
clean q3-mem q3-mem2
for round in 1 2 3 4 5; do
  for w in "q3-mem/default" "q3-mem/default,q3-mem/default" "q3-mem/default,q3-mem/second" "q3-mem/default,q3-mem/~private" "q3-mem/default,q3-mem2/default" "q3-mem/default,q3-mem/second,q3-mem/third"; do
    echo "round=$round windows=$w $(host --scenario memory --windows "$w" --exit wait | grep '^MEM ')" >> "$out"
  done
done

out="$results/profiles-clear.txt"; : > "$out"
for method in all all all 80 9180; do
  clean q4-clear
  echo "== ClearBrowsingData method=$method" >> "$out"
  host --scenario clear --windows "q4-clear/app" --clear "$method" --exit wait | grep -E "^(SIZE|CLEARED|TOKEN-SCAN|PERMISSIONS) |^RESULT .*op\":\"read" | cut -c1-700 >> "$out"
  echo "after exit: $("$exe" host --scenario scan --udf q4-clear --token CLRz8w3 | grep TOKEN-SCAN)" >> "$out"
done
for run in 1 2 3; do
  clean q4-delete
  echo "== Profile8.Delete run=$run" >> "$out"
  host --scenario clear --windows "q4-delete/app,q4-delete/keep" --clear delete --exit wait | grep -E "^(SIZE|DELETE-CALLED|DELETED|RECREATE|WAITED) " | grep -vE "RECREATE.*attempt=([2-9]|1[0-9]) " | cut -c1-300 >> "$out"
  echo "relaunch, same name:" >> "$out"
  "$exe" host --reputation off --scenario shutdown --windows "q4-delete/app" --page op=read --timeout 40 | grep -E "^(CONTROLLER|CONTROLLER-FAIL) " >> "$out"
done
clean q4-delete
for run in 1 2 3 4 5; do
  clean q4-udf
  echo "== whole-UDF delete run=$run: $(host --scenario udfdelete --windows q4-udf/default --exit wait | grep -E '^(BROWSER-EXITED|ALL-GONE|UDF-DELETED) ' | tr '\n' ' ')" >> "$out"
done

out="$results/profiles-tests.txt"; : > "$out"
for mode in freshudf freshprofile inprivate; do
  clean "q5-$mode"
  echo "== in-process mode=$mode" >> "$out"
  host --scenario tests --mode "$mode" --udf "q5-$mode" --count 10 --exit wait | grep -E "^(TEST|TEST-ENV|WAITED) " >> "$out"
  echo "left-over profile folders: $(ls "$bulky/../webview2-profiles/webview-profiles/q5-$mode/EBWebView" 2>/dev/null | grep -c WV2Profile_)" >> "$out"
  clean "q5-$mode"
done
echo "== per-process: fresh UDF per test (host waits for BrowserProcessExited, harness deletes the folder)" >> "$out"
"$exe" harness shutdown --variant per-process-fresh-udf --udf q5-per-process --runs 10 --fresh-udf --host "--reputation off --exit wait" | grep -A12 '^SUMMARY' >> "$out"
clean q5-per-process
