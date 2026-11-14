#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/startup-prerequisites/docker/cases.sh [rounds] [label regex]
# Runs every Linux case in its own throwaway container (--rm) built from the spar-startup-prereq images, with the
# spike folder mounted read-only and the case's output folder under artifacts/startup-prerequisites/linux/.
# Containers run with --security-opt seccomp=unconfined (so bubblewrap can create user namespaces as uid 1000),
# except the cases marked "default", which keep Docker's default seccomp profile.
set -u
rounds=${1:-1}
only=${2:-.}
repo=$(pwd -W 2>/dev/null || pwd)
base=artifacts/startup-prerequisites/linux
mkdir -p "$base"
webkitlib=/usr/lib/x86_64-linux-gnu
# label | image | seccomp | session | extras | break (as root) | app args
cases=(
  "nogtk-bare|base|unconfined|x11||rm -f /usr/bin/xmessage|"
  "nogtk-xmessage|base|unconfined|x11|||"
  "nogtk-kdialog|nogtk|unconfined|x11|kde||"
  "nogtk-yad|nogtk|unconfined|x11||rm -f /usr/bin/kdialog|"
  "nogtk-dbus|nogtk|unconfined|x11|dunst|rm -f /usr/bin/kdialog /usr/bin/yad|"
  "nogtk-notify-send|nogtk|unconfined|x11|dunst||--ui notify-send"
  "nogtk-nodaemon|nogtk|unconfined|x11||rm -f /usr/bin/kdialog /usr/bin/yad|"
  "gtk4-alert|gtk4|unconfined|x11|||"
  "gtk4-zenity|gtk4|unconfined|x11|||--ui zenity"
  "gtk4-alert-cairo|gtk4|unconfined|x11|cairo||"
  "gtk4-alert-wayland-cairo|gtk4|unconfined|wayland|cairo||--dialog-timeout-ms 3000"
  "gtk4-alert-wayland|gtk4|unconfined|wayland|||--dialog-timeout-ms 3000"
  "webkit-ok|webkit|unconfined|x11|||--try-view"
  "webkit-nohelpers|webkit|unconfined|x11||rm -rf $webkitlib/webkitgtk-6.0|"
  "webkit-nobwrap|webkit|unconfined|x11||rm -f /usr/bin/bwrap|"
  "webkit-noproxy|webkit|unconfined|x11||rm -f /usr/bin/xdg-dbus-proxy|"
  "webkit-sandbox-denied|webkit|default|x11|||"
  "webkit-sandbox-denied-view|webkit|default|x11|||--no-sandbox-trial --try-view --ui none"
  "webkit-old|webkit-old|unconfined|x11|||--min-webkit 2.46"
  "webkit-old-ok|webkit-old|unconfined|x11|||--try-view"
  "webkit-wayland-ok|webkit|unconfined|wayland|||--try-view"
  "nodisplay|webkit|unconfined|none|||"
  "display-gone|webkit|unconfined|none||DISPLAY_GONE|"
  "journal-devlog|gtk4|unconfined|x11|nojournal|rm -f /usr/lib/x86_64-linux-gnu/libgtk-4.so.1|"
  "journal-none|base|unconfined|none|nojournal nodevlog||"
)
for r in $(seq 1 "$rounds"); do
  for c in "${cases[@]}"; do
    IFS='|' read -r label image seccomp session extras brk args <<< "$c"
    [[ $label =~ $only ]] || continue
    out="$base/$label-r$r"
    rm -rf "${out:?}"; mkdir -p "$out"
    sec=(--security-opt seccomp=unconfined); [ "$seccomp" = default ] && sec=()
    env=()
    if [ "$brk" = DISPLAY_GONE ]; then brk=""; env=(-e SPAR_DISPLAY=localhost:7); fi
    MSYS_NO_PATHCONV=1 docker run --rm --init --shm-size=256m "${sec[@]}" "${env[@]}" --name "spar-startup-prereq-$label-r$r" \
      -v "$repo/spikes/startup-prerequisites:/spike:ro" -v "$repo/$out:/out" \
      "spar-startup-prereq:$image" bash /spike/docker/in-container.sh "$label" "$session" "$extras" "$brk" -- $args \
      > "$out/container.log" 2>&1
    tail -1 "$out/container.log"
  done
done
