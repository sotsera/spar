#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/single.sh <gtk3|gtk4> <gapp|socket>
# Question 7 in a private D-Bus session: a second launch forwards its arguments to the first. Cases: normal, first
# instance hung (SIGSTOP), first instance exiting (it keeps its name or lock for 3 s after it stops serving).
set -u
variant=$1; mech=$2
exec dbus-run-session -- bash -c '
source wsl/lib.sh
variant='"$variant"'; mech='"$mech"'
exe=$(bash wsl/exe.sh $variant)
out=$ART/runs/single-$variant-$mech; rm -rf "$out"; mkdir -p "$out"
id=org.spar.IntegProbe$RANDOM
run() { exec "$exe" --mode single --mech $mech --app-id $id "$@"; }
secondary() { # label, timeout seconds, args...
  local label=$1 limit=$2; shift 2
  local start; start=$(mono)
  timeout "$limit" "$exe" --mode single --mech $mech --app-id $id -- "$@" >"$out/$label.log" 2>&1
  local code=$? end; end=$(mono)
  echo "  secondary $label: exit=$code wall=$(python3 -I -c "print(f\"{$end-$start:.0f}\")")ms; $(grep -vE "process-start" "$out/$label.log" | sed -E "s/^M [0-9.]+ +[0-9.]+ //" | tr "\n" ";" | cut -c1-200)"
  echo "$start" >"$out/$label.start"
}
received() { # primary log, secondary label
  local line; line=$(grep -E "received|command-line remote=1" "$1" | grep -- "$2" | head -1)
  if [ -n "$line" ]; then echo "  primary received $2 $(python3 -I -c "print(f\"{float(\"$(echo "$line" | cut -d" " -f2)\")-float(open(\"$out/$2.start\").read()):.1f}\")")ms after the secondary started"; else echo "  primary did not receive $2"; fi
}
echo "== $variant $mech: normal"
run --hold-ms 4000 -- first >"$out/primary1.log" 2>&1 & p=$!
sleep 0.5; secondary second 10 second --open file.txt; sleep 0.3; received "$out/primary1.log" second; wait $p
echo "== $variant $mech: first instance hung (SIGSTOP)"
run --hold-ms 15000 -- first >"$out/primary2.log" 2>&1 & p=$!
sleep 0.5; kill -STOP $p; secondary hung-arg 8 hung-arg; kill -CONT $p; sleep 0.5; received "$out/primary2.log" hung-arg
kill $p 2>/dev/null; wait $p 2>/dev/null
echo "== $variant $mech: first instance exiting (stops serving, keeps name/lock 3 s)"
run --hold-ms 1000 --slow-exit-ms 3000 -- first >"$out/primary3.log" 2>&1 & p=$!
sleep 1.4; secondary exiting-arg 10 exiting-arg; wait $p; received "$out/primary3.log" exiting-arg
grep -hE "received|command-line|shutdown|exiting|exited" "$out/primary3.log" | sed -E "s/^M [0-9.]+ +/  primary3 /" | cut -c1-140
'
