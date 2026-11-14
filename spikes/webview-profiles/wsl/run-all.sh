#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/webview-profiles): bash wsl/run-all.sh <gtk3|gtk4>
# Question 6: the Linux counterparts of questions 1-5, each in its own D-Bus session on a private Broadway display.
# Summaries go to results/linux-<variant>-*.txt (copied back by the caller), raw records to artifacts/webview-profiles.
set -u
source wsl/env.sh
v=$1
run() { bash wsl/session.sh "$v" "$@" 2>>"../../artifacts/webview-profiles/linux-$v-stderr.log"; }
mkdir -p results ../../artifacts/webview-profiles

out=results/linux-$v-shutdown.txt; : > "$out"
sd() { local name=$1; shift; echo "== $name" >> "$out"; run harness shutdown --variant "$name" --windows "q1$name" --runs "${RUNS:-30}" "$@" | tee "../../artifacts/webview-profiles/linux-$v-q1-$name.txt" | grep -A12 '^SUMMARY' >> "$out"; run harness clean --windows "q1$name"; }
sd default
sd unload --page-extra "&unload=1"
sd idbstream --page-extra "&stream=1"
sd killhost --kill
sd waitexit --host "--exit wait"
sd linger2s --linger 2000 --runs 10

out=results/linux-$v-relaunch.txt; : > "$out"
echo "== relaunch" >> "$out"
run harness relaunch --windows q2relaunch --runs 30 | grep -A2 '^SUMMARY' >> "$out"
run harness clean --windows q2relaunch
for i in 1 2; do echo "== concurrent $i" >> "$out"; run harness concurrent --windows q2conc >> "$out"; done
run harness clean --windows q2conc

out=results/linux-$v-isolation.txt; : > "$out"
for n in q3a q3b; do run harness clean --windows $n; done
echo "== multi: a, a, b, ephemeral" >> "$out"
run host --scenario multi --windows "q3a,q3a,q3b,~ephemeral" --exit wait | grep -E "^(SESSION|RESULT|TOKEN-SCAN|SIZE|CHILDREN-EXITED) " | cut -c1-700 >> "$out"
echo "after exit: $(run host --scenario scan --token EPHx7q9 | grep TOKEN-SCAN)" >> "$out"
for spec in "~ephemeral:x,~ephemeral:x"; do
  echo "== two views on one ephemeral data set: $spec" >> "$out"
  run host --scenario multi --windows "$spec" --exit wait | grep -E "^RESULT " | cut -c1-300 >> "$out"
done

out=results/linux-$v-memory.txt; : > "$out"
for round in 1 2 3 4 5; do
  for w in "q3a" "q3a,q3a" "q3a,q3b" "~ephemeral" "q3a,~ephemeral" "q3a,q3b,q3c"; do
    echo "round=$round windows=$w $(run host --scenario memory --windows "$w" --exit wait | grep '^MEM ')" >> "$out"
  done
done
for n in q3a q3b q3c; do run harness clean --windows $n; done

out=results/linux-$v-clear.txt; : > "$out"
for types in all all all localidb; do
  run harness clean --windows q4clear
  arg=$types; [ "$types" = localidb ] && arg=$([ "$v" = gtk4 ] && echo 30 || echo 50)
  echo "== clear types=$types ($arg)" >> "$out"
  run host --scenario clear --windows q4clear --clear "$arg" --exit wait | grep -E "^(SIZE|CLEARED|TOKEN-SCAN|FILES|CHILDREN-EXITED) |^RESULT .*op\":\"read" | cut -c1-900 >> "$out"
done
run harness clean --windows q4clear

out=results/linux-$v-tests.txt; : > "$out"
for mode in freshdirs ephemeral shared-clear; do
  echo "== in-process mode=$mode" >> "$out"
  run host --scenario tests --mode "$mode" --count 10 --exit wait | grep -E "^(TEST|CHILDREN-EXITED) " >> "$out"
done
run harness clean --windows tshared
echo "== per-process: fresh folders per test" >> "$out"
run harness shutdown --variant perprocess --windows q5perprocess --runs 10 --fresh-udf --host "--exit wait" | grep -A12 '^SUMMARY' >> "$out"
run harness clean --windows q5perprocess
echo "done $v"
