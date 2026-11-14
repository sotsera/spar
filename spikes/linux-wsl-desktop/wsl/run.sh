#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/run.sh <gtk3|gtk3sb|gtk4> <label> [app args...]
# Runs the host once. The display backend comes from the environment (GDK_BACKEND=x11 for X11; Wayland otherwise).
# XDG_CACHE_HOME points at a per-profile folder under artifacts/, so Mesa, GStreamer, fontconfig and the 6.0 sandbox
# do not write into the user's ~/.cache; --fresh deletes it together with the WebKit data. Output: the host's log in
# artifacts/linux-wsl-desktop/runs/<label>/run.log (timeline.json is written by the host).
set -u
variant=$1; label=$2; shift 2
dir=Wsl.Linux/bin/Release/net11.0/linux-x64/publish
extra=()
case $variant in
  gtk4) dir=Wsl.Linux/bin/gtk4/Release/net11.0/linux-x64/publish ;;
  gtk3sb) extra=(--sandbox) ;;
esac
art=../../artifacts/linux-wsl-desktop
out=$art/runs/$label
rm -rf "$out"; mkdir -p "$out"
profile=$label
for ((i = 1; i <= $#; i++)); do [ "${!i}" = --profile ] && { j=$((i + 1)); profile=${!j}; }; done
export XDG_CACHE_HOME=$(mkdir -p "$art/cache" && cd "$art/cache" && pwd)/$profile
case " $* " in *" --fresh "*) rm -rf "$XDG_CACHE_HOME" ;; esac
mkdir -p "$XDG_CACHE_HOME"
echo "SPAR_RUN_START $variant $label backend=${GDK_BACKEND:-wayland(default)} env: $(env | grep -E '^(WEBKIT_|GDK_|GSK_|LIBGL|MESA|GALLIUM)' | tr '\n' ' ')" >"$out/run.log"
# WCAP=1 with the X11 backend: capture the window from X11 (wsl/wcap) into the run folder, merged later by stamps.
cap=
if [ "${WCAP:-0}" = 1 ] && [ "${GDK_BACKEND:-}" = x11 ]; then wsl/wcap "$out" "Spar WSL $label" 6000 2>>"$out/run.log" & cap=$!; sleep 0.05; fi
start=$(date +%s%N)
timeout 90 "$dir/Wsl.Linux" --label "$label" "${extra[@]}" "$@" >>"$out/run.log" 2>&1
code=$?
[ -n "$cap" ] && { kill -TERM "$cap" 2>/dev/null; wait "$cap"; }
wall=$(( ($(date +%s%N) - start) / 1000000 ))
# Zombies (exited, not yet reaped after the host exited) are not counted.
left=$(ps -u "$(id -u)" -o pid=,stat=,comm= | awk '$2 !~ /^Z/ && $3 ~ /^(WebKit|Wsl.Linux|bwrap|xdg-dbus-proxy)/ {print $1, $3}' | tr '\n' ' ')
echo "SPAR_RUN $variant $label exit=$code wall=${wall}ms processes-left=[$left]" | tee -a "$out/run.log"
