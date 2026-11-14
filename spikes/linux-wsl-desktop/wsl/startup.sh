#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/startup.sh <set> <rounds> [app args...]
# Startup matrix: for each round, for each backend (Wayland, X11) and variant (gtk3, gtk3sb, gtk4), one cold run
# (fresh WebKit data and XDG cache) and one warm run (a profile primed before round 1). Runs are interleaved so drift
# spreads over all combinations. The renderer environment (WEBKIT_DISABLE_*) is taken from the caller.
# Labels: <set>-<backend>-<variant>-<cold|warm>-<round>.
set -u
set_name=$1; rounds=$2; shift 2
backends=${BACKENDS:-wayland x11}
variants=${VARIANTS:-gtk3 gtk3sb gtk4}
run() {
  local backend=$1; shift
  if [ "$backend" = x11 ]; then GDK_BACKEND=x11 bash wsl/run.sh "$@" --after-ready 300 "${extra[@]}"; else bash wsl/run.sh "$@" --after-ready 300 "${extra[@]}"; fi
}
extra=("$@")
for b in $backends; do for v in $variants; do run "$b" "$v" "$set_name-$b-$v-prime" --profile "$set_name-$b-$v-warm" --fresh >/dev/null; done; done
for i in $(seq 1 "$rounds"); do
  for b in $backends; do for v in $variants; do
    run "$b" "$v" "$set_name-$b-$v-cold-$i" --profile "$set_name-$b-$v-cold" --fresh | tail -1
    run "$b" "$v" "$set_name-$b-$v-warm-$i" --profile "$set_name-$b-$v-warm" | tail -1
  done; done
done
