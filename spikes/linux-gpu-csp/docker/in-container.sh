#!/usr/bin/env bash
# Runs inside a spar-spike-linux-gpu-csp image as a non-root user. Usage: in-container.sh <task> [args]
#   series <rounds> <cold>  every detection method (no display variables)
#   gdk <rounds> <cold>     GDK's signal and the GSK renderer under Xvfb
#   csp                     the connect-src probes, profiles spar, self and none
#   accel                   webkit://gpu, GSK renderer and memory: defaults against the chosen software-GPU default
# /spike is the spike folder (read-only), /probe holds GpuProbe, /out is this environment's results folder.
set -uo pipefail
task=$1; shift
mkdir -p /tmp/h/.config /tmp/h/.cache /tmp/h/.local/share /tmp/h/.local/state /tmp/run
chmod 700 /tmp/run
export HOME=/tmp/h XDG_RUNTIME_DIR=/tmp/run XDG_CONFIG_HOME=/tmp/h/.config XDG_CACHE_HOME=/tmp/h/.cache
export XDG_DATA_HOME=/tmp/h/.local/share XDG_STATE_HOME=/tmp/h/.local/state NO_AT_BRIDGE=1 GTK_A11Y=none
unset WAYLAND_DISPLAY DISPLAY GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER
cp /probe/GpuProbe /tmp/GpuProbe
probe=/tmp/GpuProbe
xvfb() {
  Xvfb :99 -screen 0 1280x1024x24 -nolisten tcp >/tmp/xvfb.log 2>&1 &
  for _ in $(seq 1 100); do [ -S /tmp/.X11-unix/X99 ] && break; sleep 0.05; done
  export DISPLAY=:99 GDK_BACKEND=x11
}
versions() {
  { id; uname -r; cat /etc/os-release | grep PRETTY; ls -l /dev/dri /dev/dxg 2>&1;
    (dpkg -l 2>/dev/null | grep -E "libwebkitgtk-6.0-4|mesa-libgallium|libgtk-4-1 " | awk '{print $2, $3}');
    (rpm -q webkitgtk6.0 gtk4 mesa-dri-drivers mesa-libEGL 2>/dev/null); true; } > /out/versions.txt 2>&1
}
case "$task" in
  series)
    versions
    rm -f /out/series.jsonl
    python3 -I /spike/tools/series.py "$probe" /out/series.jsonl "$LABEL" "$1" "$2" \
      none node combined devices egl+--platform+surfaceless+--no-context egl+--platform+surfaceless \
      egl+--platform+device egl+--platform+default
    ;;
  gdk)
    xvfb
    rm -f /out/gdk.jsonl
    dbus-run-session -- python3 -I /spike/tools/series.py "$probe" /out/gdk.jsonl "$LABEL-xvfb" "$1" "$2" \
      gdk gdk+--window gdk+--window+--gsk-after-init+cairo gdk+--pre-egl+--window
    ;;
  csp)
    xvfb
    mkdir -p /out/csp
    for profile in spar self none; do
      for i in 1 2; do
        dbus-run-session -- python3 -I /spike/csp/csp_host.py /spike/csp/pages "/out/csp/$profile-$i.json" \
          --profile "$profile" --label "$LABEL-$profile-$i" >"/out/csp/$profile-$i.log" 2>&1
        echo "csp $profile-$i exit=$?"
      done
    done
    ;;
  accel)
    xvfb
    mkdir -p /out/accel
    run() {
      local name=$1 policy=$2; shift 2
      for i in 1 2 3; do
        env "$@" dbus-run-session -- python3 -I /spike/csp/accel_host.py "/out/accel/$name-$i.json" --policy "$policy" \
          --label "$name-$i" >"/out/accel/$name-$i.log" 2>&1
        echo "accel $name-$i exit=$?"
      done
    }
    run defaults default
    run never-cairo never GSK_RENDERER=cairo
    run dmabuf-off-cairo default GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1
    run never-only never
    ;;
esac
# forced <rounds>: the hardware control, GALLIUM_DRIVER=d3d12 (only meaningful in the dxg containers).
if [ "$task" = forced ]; then
  export GALLIUM_DRIVER=d3d12
  rm -f /out/forced-d3d12.jsonl
  python3 -I /spike/tools/series.py "$probe" /out/forced-d3d12.jsonl "$LABEL-d3d12" "$1" 0 \
    combined egl+--platform+surfaceless egl+--platform+surfaceless+--no-context
  xvfb
  dbus-run-session -- python3 -I /spike/tools/series.py "$probe" /out/forced-d3d12.jsonl "$LABEL-d3d12-xvfb" "$1" 0 gdk+--window
fi
