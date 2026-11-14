#!/usr/bin/env bash
# Usage (in the container, in /work/spikes/theme-startup, as uid 1000 with DISPLAY set):
#   linux/run-sets.sh <gtk3|gtk4> <set> [warm runs]
# The Linux counterpart of run-windows.sh: one cold run (--fresh WebKit data) and N warm runs per configuration.
set -u
variant=$1; set_name=$2; warm=${3:-2}
log=../../artifacts/theme-startup/linux-$variant-$set_name.log
: >"$log"

run() { local label=$1; shift; echo "=== $label $*" >>"$log"; timeout 60 linux/run.sh "$variant" "$label" "$@" >>"$log" 2>&1 || echo "EXIT $? $label" >>"$log"; }
series() {
  local name=$1; shift
  local base="$variant-$set_name-$name"
  run "$base-cold" --profile "$base" --fresh "$@"
  for i in $(seq 1 "$warm"); do run "$base-warm$i" --profile "$base" "$@"; done
}

case $set_name in
  scheme)
    # What drives prefers-color-scheme in WebKitGTK (theme dark, nothing else set): read from the page's report.
    common="--theme dark --after-ready 200 --page themed.html"
    series none        $common
    series prefer-dark $common --layers prefer-dark
    series gtk-theme   $common --layers gtk-theme
    GTK_THEME=Adwaita:dark series env-gtk-theme $common
    ;;
  layers)
    for theme in dark light; do
      common="--theme $theme --after-ready 300 --headerbar"
      series "$theme-L0-none"        $common --page baseline.html
      series "$theme-L1-css"         $common --page baseline.html --layers css
      series "$theme-L2-prefer-dark" $common --page baseline.html --layers css,prefer-dark
      series "$theme-L3-wvbg"        $common --page baseline.html --layers css,prefer-dark,wv-bg
      series "$theme-L4-page"        $common --page themed.html   --layers css,prefer-dark,wv-bg
      series "$theme-L4t-transparent" $common --page themed.html  --layers css,prefer-dark,transparent
      series "$theme-P-pageonly"     $common --page themed.html
    done
    ;;
  slowhtml)
    for theme in dark light; do
      common="--theme $theme --after-ready 200 --headerbar --html-delay 1000"
      series "$theme-css"         $common --page themed.html --layers css,prefer-dark
      series "$theme-wvbg"        $common --page themed.html --layers css,prefer-dark,wv-bg
      series "$theme-transparent" $common --page themed.html --layers css,prefer-dark,transparent
    done
    ;;
  splash)
    all="--layers css,prefer-dark,wv-bg --headerbar --after-ready 300"
    for theme in dark light; do
      series "$theme-fast"      --theme $theme $all --page splash.html
      series "$theme-b300"      --theme $theme $all --page splash.html --bundle-delay 300
      series "$theme-b600-w600" --theme $theme $all --page splash.html --bundle-delay 600 --query work=600
      series "$theme-b1200"     --theme $theme $all --page splash.html --bundle-delay 1200
      series "$theme-w1500"     --theme $theme $all --page splash.html --query work=1500
    done
    series "dark-manual-d1000"  --theme dark $all --page splash.html --query "manual=1&data=1000"
    series "dark-reduced-b1200" --theme dark $all --page splash.html --bundle-delay 1200 --reduced-motion
    series "dark-nohash-b1200"  --theme dark $all --page splash.html --bundle-delay 1200 --no-hash
    ;;
  show)
    all="--layers css,prefer-dark,wv-bg --headerbar --after-ready 300 --page themed.html"
    for theme in dark light; do
      series "$theme-now"   --theme $theme $all --show now
      series "$theme-ready" --theme $theme $all --show ready --show-timeout 3000
    done
    ;;
  live)
    all="--layers css,prefer-dark,wv-bg --headerbar --after-ready 300 --page themed.html --live"
    for start in dark light; do
      rm -rf "../../artifacts/theme-startup/appdata/linux-$variant-live-$start"
      series "$start" --system $start --appdata "linux-$variant-live-$start" $all
    done
    ;;
  persist)
    all="--layers css,prefer-dark,wv-bg --headerbar --after-ready 300 --page themed.html"
    d=../../artifacts/theme-startup/appdata
    mkdir -p "$d/linux-persist-dark" "$d/linux-persist-light"
    printf dark >"$d/linux-persist-dark/color-scheme.txt"; printf light >"$d/linux-persist-light/color-scheme.txt"
    series "saved-dark-system-light" --system light --appdata linux-persist-dark $all
    series "saved-light-system-dark" --system dark --appdata linux-persist-light $all
    series "saved-dark-no-system"    --appdata linux-persist-dark $all
    ;;
  *) echo "unknown set $set_name"; exit 2 ;;
esac
echo "done $variant $set_name" >>"$log"
