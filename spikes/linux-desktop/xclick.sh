#!/usr/bin/env bash
# Usage (in the container, from this folder): xclick.sh <gtk3|gtk4> [app args...]
# Trusted input from outside the process: the self-test prints the Save button's position, xdotool clicks it through
# XTEST on the X server, and the page reports whether the click was trusted.
set -u
variant=$1; shift
log=../../artifacts/linux-desktop/xclick-$variant.log
./run.sh "$variant" "xclick-$variant" --selftest --external-click "$@" >/dev/null &
runner=$!
until grep -q SPAR_CLICK_AT "$log" 2>/dev/null; do sleep 0.01; done
read -r x y < <(grep -m1 SPAR_CLICK_AT "$log" | awk '{print $2, $3}')
xdotool mousemove "$x" "$y" click 1
wait $runner
grep -E "SPAR_CLICK_AT|SPAR_EXTERNAL_CLICK|SPAR_RUN" "$log"
