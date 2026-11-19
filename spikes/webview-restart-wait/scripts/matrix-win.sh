#!/usr/bin/env bash
# The Windows matrix of spikes/webview-restart-wait: every cell is one probe process with 10 restarts (cycles 1-10)
# after a first controller (cycle 0). Raw logs in results/win/<cell>.log; one summary line per cell in
# results/win/matrix-summary.txt. Pass cell-name prefixes to run only those groups.
set -u
spike=$(cd "$(dirname "$0")/.." && pwd)
run() {
  local name=$1
  shift
  if [ -n "${only:-}" ] && ! printf '%s' "$name" | grep -Eq "^($only)"; then return; fi
  "$spike/scripts/win.sh" run "$name" --reps 10 "$@" | grep summary >> "$spike/results/win/matrix-summary.txt"
}
only=$(IFS='|'; echo "$*")
mkdir -p "$spike/results/win"

# Gap x SmartScreen, close right after the first navigation, default profile, shown offscreen, one environment.
for gap in 0 100 250 500 1000 2000 3000 5000 8000; do
  run "gap-on-$gap" --gap "$gap" --smartscreen on
  run "gap-off-$gap" --gap "$gap" --smartscreen off
done

# Idle before close: 3 s between the navigation and the close.
run idle-on-0 --gap 0 --smartscreen on --dwell 3000
run idle-on-1000 --gap 1000 --smartscreen on --dwell 3000

# Profile kinds.
for profile in private named; do
  run "$profile-on-0" --gap 0 --smartscreen on --profile "$profile"
  run "$profile-on-1000" --gap 1000 --smartscreen on --profile "$profile"
  run "$profile-off-0" --gap 0 --smartscreen off --profile "$profile"
done

# A hidden (never shown) window instead of a shown offscreen one.
run hidden-on-1000 --gap 1000 --smartscreen on --window hidden
run hidden-off-0 --gap 0 --smartscreen off --window hidden

# A new environment object for each controller (same folder).
run envnew-on-0 --gap 0 --smartscreen on --env new
run envnew-on-1000 --gap 1000 --smartscreen on --env new
run envnew-off-0 --gap 0 --smartscreen off --env new

# Candidate fixes.
run waitevent-on-0 --gap 0 --smartscreen on --wait event
run waitevent-off-0 --gap 0 --smartscreen off --wait event
run waitprocess-on-0 --gap 0 --smartscreen on --wait process
run waitprocess-off-0 --gap 0 --smartscreen off --wait process
run percycle-on-0 --gap 0 --smartscreen on --folder per-cycle
run percycle-off-0 --gap 0 --smartscreen off --folder per-cycle
run overlap-on --gap 0 --smartscreen on --overlap true
run overlap-off --gap 0 --smartscreen off --overlap true
run disablefeature-on-1000 --gap 1000 --smartscreen on --args "--disable-features=msSmartScreenProtection"
echo "matrix done"
