#!/usr/bin/env bash
# Usage (Git Bash on Windows, from spikes/linux-wsl-desktop): win/theme-runs.sh
# The theme layers from spikes/theme-startup on WSLg: each configuration on GTK3 and GTK4, Wayland and X11, a fresh run
# then a warm run. Wayland frames come from the Windows capture; X11 frames from both the Windows capture and wcap.
set -u
web="--web ../theme-startup/frontend/dist --order load-first --after-ready 400"
configs=(
  "dark-L0|--theme dark --page baseline.html"
  "dark-L3|--theme dark --layers css,prefer-dark,wv-bg --page baseline.html"
  "dark-L4|--theme dark --layers css,prefer-dark,wv-bg --page themed.html"
  "light-L4|--theme light --layers css,prefer-dark,wv-bg --page themed.html"
  "dark-L4t|--theme dark --layers css,prefer-dark,transparent --page themed.html"
)
for entry in "${configs[@]}"; do
  name=${entry%%|*}; args=${entry#*|}
  for b in wayland x11; do for v in gtk3 gtk4; do
    envs="WCAP=0"; [ $b = x11 ] && envs="WCAP=1 GDK_BACKEND=x11"
    for pass in fresh warm; do
      label=th-$name-$b-$v-$pass
      extra="--profile th-$name-$b-$v"; [ $pass = fresh ] && extra="$extra --fresh"
      timeout 120 bash win/capture-run.sh "-Frames -DurationMs 1500" "$envs" $v $label $web $args $extra </dev/null | tail -1
    done
  done; done
done
