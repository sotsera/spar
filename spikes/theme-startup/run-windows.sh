#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/theme-startup/run-windows.sh <set> [warm runs]
# Runs the Windows host for one measurement set. Each configuration gets its own profile: the first run is cold
# (--fresh: a new profile), the next ones warm. Output: artifacts/theme-startup/<label>/timeline.json and PNGs,
# plus artifacts/theme-startup/<set>.log with the host output.
set -u
set_name=$1; warm=${2:-3}
exe=spikes/theme-startup/Theme.Desktop/bin/Release/net11.0/win-x64/publish/Theme.Desktop.exe
log=artifacts/theme-startup/$set_name.log
mkdir -p artifacts/theme-startup
: >"$log"

run() { # label profile args...
  local label=$1; shift
  echo "=== $label $*" >>"$log"
  timeout 90 "$exe" --label "$label" "$@" >>"$log" 2>&1 || echo "EXIT $? $label" >>"$log"
}

series() { # name extra-args... : one cold run and $warm warm runs
  local name=$1; shift
  run "$set_name-$name-cold" --profile "$set_name-$name" --fresh "$@"
  for i in $(seq 1 "$warm"); do run "$set_name-$name-warm$i" --profile "$set_name-$name" "$@"; done
}

case $set_name in
  layers)
    # Question 1 and 2: each layer added in turn, dark (the system's theme here) and light (the app's choice).
    for theme in dark light; do
      common="--theme $theme --capture 2500 --after-ready 300"
      series "$theme-L0-none"            $common --page baseline.html
      series "$theme-L1-brush"           $common --page baseline.html --layers brush
      series "$theme-L2-titlebar"        $common --page baseline.html --layers brush,titlebar
      series "$theme-L3a-wvput"          $common --page baseline.html --layers brush,titlebar,wv-put
      series "$theme-L3b-wvenv"          $common --page baseline.html --layers brush,titlebar,wv-env
      series "$theme-L3c-wvoptions"      $common --page baseline.html --layers brush,titlebar,wv-options
      series "$theme-L4-scheme"          $common --page baseline.html --layers brush,titlebar,wv-options,scheme
      series "$theme-L5-page"            $common --page themed.html   --layers brush,titlebar,wv-options,scheme
      series "$theme-P-pageonly"         $common --page themed.html
    done
    ;;
  firstframe)
    # The first frame after ShowWindow: plain, + UpdateWindow, or shown cloaked, painted, then uncloaked.
    for theme in dark light; do
      common="--theme $theme --capture 1200 --after-ready 100 --page themed.html"
      series "$theme-plain"      $common --layers brush,titlebar,wv-options,scheme
      series "$theme-update"     $common --layers brush,titlebar,wv-options,scheme,update
      series "$theme-cloakpaint" $common --layers brush,titlebar,wv-options,scheme,cloakpaint
      series "$theme-none"       $common --page baseline.html
      series "$theme-none-update" $common --page baseline.html --layers update
    done
    ;;
  slowhtml)
    # What the WebView shows while the document is slow to arrive (HTML response delayed 1 s), per WebView layer.
    for theme in dark light; do
      common="--theme $theme --capture 2500 --after-ready 100 --html-delay 1000"
      series "$theme-none"      $common --page baseline.html
      series "$theme-brush"     $common --page baseline.html --layers brush,titlebar
      series "$theme-wvput"     $common --page baseline.html --layers brush,titlebar,wv-put
      series "$theme-wvenv"     $common --page baseline.html --layers brush,titlebar,wv-env
      series "$theme-wvoptions" $common --page baseline.html --layers brush,titlebar,wv-options
      series "$theme-scheme"    $common --page baseline.html --layers brush,titlebar,wv-options,scheme
      series "$theme-all"       $common --page themed.html   --layers brush,titlebar,wv-options,scheme
      series "$theme-transparent" $common --page themed.html --layers brush,titlebar,transparent,wv-options,scheme
    done
    ;;
  navdelay)
    # The controller exists for 1 s before Navigate: does it paint, and in which colour, per WebView layer?
    for theme in dark light; do
      common="--theme $theme --capture 2500 --after-ready 100 --navigate-delay 1000 --page themed.html"
      series "$theme-brush"       $common --layers brush,titlebar
      series "$theme-wvput"       $common --layers brush,titlebar,wv-put
      series "$theme-wvenv"       $common --layers brush,titlebar,wv-env
      series "$theme-wvoptions"   $common --layers brush,titlebar,wv-options
      series "$theme-scheme"      $common --layers brush,titlebar,wv-options,scheme
      series "$theme-transparent" $common --layers brush,titlebar,transparent,wv-options,scheme
    done
    ;;
  cssdelay)
    # The app stylesheet arrives 1 s late: what the committed page shows without and with the theme style.
    for theme in dark light; do
      common="--theme $theme --capture 2500 --after-ready 100 --css-delay 1000 --layers brush,titlebar,transparent,wv-options,scheme"
      series "$theme-baseline" $common --page baseline.html
      series "$theme-themed"   $common --page themed.html
    done
    ;;
  validate)
    # Both captures in the same run (PrintWindow and Windows.Graphics.Capture), to check that they agree.
    for theme in dark light; do
      common="--theme $theme --capture 2500 --after-ready 300 --capture-with both"
      series "$theme-L0"           $common --page baseline.html
      series "$theme-L1-brush"     $common --page baseline.html --layers brush
      series "$theme-all"          $common --page themed.html --layers brush,titlebar,wv-options,scheme
      series "$theme-all-slowhtml" $common --page themed.html --layers brush,titlebar,wv-options,scheme --html-delay 1000
      series "$theme-transparent-slowhtml" $common --page themed.html --layers brush,titlebar,transparent,wv-options,scheme --html-delay 1000
    done
    ;;
  splash)
    # Question 5: the splash with a fast app and slow apps (bundle delayed, start-up work), and its variants.
    # splash*.html is the cover design (v2); splash-v1.html the first design, kept for comparison.
    all="--layers brush,titlebar,transparent,wv-options,scheme --capture 5000 --after-ready 300"
    for theme in dark light; do
      series "$theme-fast"          --theme $theme $all --page splash.html
      series "$theme-b300"          --theme $theme $all --page splash.html --bundle-delay 300
      series "$theme-b600-w600"     --theme $theme $all --page splash.html --bundle-delay 600 --query work=600
      series "$theme-b1200"         --theme $theme $all --page splash.html --bundle-delay 1200
      series "$theme-b2000"         --theme $theme $all --page splash.html --bundle-delay 2000
      series "$theme-w1500"         --theme $theme $all --page splash.html --query work=1500
    done
    series "dark-manual-d1000"      --theme dark $all --page splash.html --query "manual=1&data=1000"
    series "dark-v1-manual-d1000"   --theme dark $all --page splash-v1.html --query "manual=1&data=1000"
    series "dark-v1-b1200"          --theme dark $all --page splash-v1.html --bundle-delay 1200
    series "dark-manual-d150"       --theme dark $all --page splash.html --query "manual=1&data=150"
    series "dark-200-b600"          --theme dark $all --page splash-200.html --bundle-delay 600
    series "dark-500-b600"          --theme dark $all --page splash-500.html --bundle-delay 600
    series "dark-text-b600"         --theme dark $all --page splash-text.html --bundle-delay 600
    series "dark-text-fast"         --theme dark $all --page splash-text.html
    series "dark-reduced-b1200"     --theme dark $all --page splash.html --bundle-delay 1200 --reduced-motion
    series "dark-nohash-b1200"      --theme dark $all --page splash.html --bundle-delay 1200 --no-hash
    ;;
  show)
    # Question 3: show at once (right colour) against show at ui-ready (hidden, or cloaked) with a 3 s timeout.
    all="--layers brush,titlebar,transparent,wv-options,scheme --capture 4000 --after-ready 300 --page themed.html"
    for theme in dark light; do
      series "$theme-now"         --theme $theme $all --show now
      series "$theme-ready"       --theme $theme $all --show ready --show-timeout 3000
      series "$theme-cloak"       --theme $theme $all --show cloak --show-timeout 3000
      series "$theme-now-slow"    --theme $theme $all --show now --bundle-delay 1200
      series "$theme-cloak-slow"  --theme $theme $all --show cloak --show-timeout 3000 --bundle-delay 1200
    done
    ;;
  live)
    # Question 4: system changes (simulated: WM_SETTINGCHANGE to our own window) and the app's own switches.
    all="--layers brush,titlebar,transparent,wv-options,scheme --capture 7000 --after-ready 300 --page themed.html --live"
    for start in dark light; do
      rm -rf "artifacts/theme-startup/appdata/live-$start"
      series "$start" --system $start --appdata live-$start $all
    done
    ;;
  persist)
    # Question 4: the app's saved choice is applied before the page loads at the next start.
    all="--layers brush,titlebar,transparent,wv-options,scheme --capture 2500 --after-ready 300 --page themed.html"
    mkdir -p artifacts/theme-startup/appdata/persist-dark artifacts/theme-startup/appdata/persist-light
    printf dark > artifacts/theme-startup/appdata/persist-dark/color-scheme.txt
    printf light > artifacts/theme-startup/appdata/persist-light/color-scheme.txt
    series "saved-dark-system-light"  --system light --appdata persist-dark $all
    series "saved-light-system-dark"  --system dark --appdata persist-light $all
    series "saved-light-real-system"  --appdata persist-light $all
    ;;
  showfix)
    # Show at ui-ready with a hidden window, plus put_IsVisible(false/true) on the controller once shown.
    for theme in dark light; do
      series "$theme-ready-revisible" --theme $theme --layers brush,titlebar,transparent,wv-options,scheme,revisible --capture 4000 --after-ready 300 --page themed.html --show ready --show-timeout 3000
    done
    ;;
  wvwhite)
    # Does the controller paint its default white, and before put_DefaultBackgroundColor? Light scheme (white is
    # the default), controller alive 1 s before Navigate.
    common="--theme light --capture 2500 --after-ready 100 --navigate-delay 1000 --page themed.html"
    series "light-scheme-nowv"   $common --layers brush,titlebar,scheme
    series "light-scheme-wvput"  $common --layers brush,titlebar,scheme,wv-put
    series "light-scheme-wvenv"  $common --layers brush,titlebar,scheme,wv-env
    series "dark-scheme-nowv"    --theme dark --capture 2500 --after-ready 100 --navigate-delay 1000 --page themed.html --layers brush,titlebar,scheme
    ;;
  splashfix)
    # The splash's visible time counted from the first rendered frame when start-up work blocked rendering (SDK rule 2),
    # against the animation timeline only (?timelineOnly=1, rule 1).
    all="--layers brush,titlebar,transparent,wv-options,scheme --capture 5000 --after-ready 300"
    for theme in dark light; do
      series "$theme-b600-w600"       --theme $theme $all --page splash.html --bundle-delay 600 --query work=600
      series "$theme-b600-w600-rule1" --theme $theme $all --page splash.html --bundle-delay 600 --query "work=600&timelineOnly=1"
      series "$theme-b1200"           --theme $theme $all --page splash.html --bundle-delay 1200
      series "$theme-w1500"           --theme $theme $all --page splash.html --query work=1500
      series "$theme-fast"            --theme $theme $all --page splash.html
    done
    ;;
  *) echo "unknown set $set_name"; exit 2 ;;
esac
echo "done $set_name" >>"$log"
