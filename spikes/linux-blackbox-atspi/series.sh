#!/usr/bin/env bash
# The measurement series behind RESULTS.md, in order (inside WSL; each `run` has its own Xvfb, session and homes).
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/linux-blackbox-atspi/series.sh [parts...]
# Parts: env window relaunch kill dispose hang (all by default).
here=$(cd "$(dirname "$0")" && pwd)
run() { bash "$here/wsl.sh" run "$@"; }
for part in ${@:-env window relaunch kill dispose hang}; do
  case $part in
    env)
      run standard env default; run standard env gtk-a11y-none; run standard env no-at-bridge; run standard env events
      run bare env default; run bare env private; run none env private ;;
    window) run standard window 20 ;;
    relaunch) run standard relaunch 20 ;;
    kill) run standard kill plain,kpe,kpe-thread,setpriv,setpriv-thread,session 0,20,100,300,ready 5 ;;
    dispose) run standard dispose kill,tree,term,subreaper 10 ;;
    hang) run standard hang 20 ;;
  esac
done
