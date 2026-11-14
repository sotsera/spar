#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-memory): bash wsl/matrix.sh <main|floors> <last round> [first round]
# main: each API x backend x rendering configuration, points ready, idle30, heavy.
# floors: C + GTK window, AOT host GTK-only window, AOT host with an empty WebKit page; points ready, idle30.
# Runs are sequential (PSS divides shared pages among all processes that map them). Round-robin order, so the Mesa
# shader cache and the OS page cache are warm for all but the first round.
set -u
source wsl/env.sh
set_cfg() {  # sets ENVS and ARGS for a configuration name
  ENVS=(); ARGS=()
  case $1 in
    def) ;;
    *noaccel-cairo) ENVS=(WEBKIT_DISABLE_DMABUF_RENDERER=1 GSK_RENDERER=cairo) ;;
    *noaccel) ENVS=(WEBKIT_DISABLE_DMABUF_RENDERER=1) ;;
    *cairo) ENVS=(GSK_RENDERER=cairo) ;;
    sandbox) ARGS=(--sandbox) ;;
  esac
}
one() {  # variant backend cfg round points [extra args]
  local variant=$1 backend=$2 cfg=$3 round=$4 points=$5; shift 5
  set_cfg "$cfg"
  local label="$variant-$backend-$cfg-$round"
  echo "== $label $(date +%T)"
  env GDK_BACKEND=$backend "${ENVS[@]}" bash wsl/session.sh "$variant" "$label" "$points" "${ARGS[@]}" "$@"
}
mode=$1; rounds=$2; first=${3:-1}
for round in $(seq "$first" "$rounds"); do
  for backend in wayland x11; do
    if [ "$mode" = main ]; then
      for cfg in def noaccel sandbox; do one gtk3 "$backend" "$cfg" "r$round" ready,idle30,heavy; done
      for cfg in def cairo noaccel noaccel-cairo; do one gtk4 "$backend" "$cfg" "r$round" ready,idle30,heavy; done
    else
      for v in floor-gtk3 floor-gtk4; do one "$v" "$backend" def "r$round" ready,idle30; done
      for v in gtk3 gtk4; do
        one "$v" "$backend" gtkonly "r$round" ready,idle30 --mode gtk
        one "$v" "$backend" empty "r$round" ready,idle30 --page empty
      done
      one gtk3 "$backend" empty-noaccel "r$round" ready,idle30 --page empty
      one gtk4 "$backend" empty-noaccel-cairo "r$round" ready,idle30 --page empty
      one gtk4 "$backend" gtkonly-cairo "r$round" ready,idle30 --mode gtk
    fi
  done
done
echo "== done $(date +%T)"
