#!/usr/bin/env bash
# Runs inside a throwaway container as root (started by docker/run.sh): in-container.sh <group> <runs>
# Starts, as uid 1000 with private HOME, XDG folders and XDG_RUNTIME_DIR under /tmp/u: a session D-Bus, Xvfb :99 and
# openbox, then docker/cases.sh. WebKitGTK 6.0's sandbox (bubblewrap) needs the non-root user.
set -u
mkdir -p /out && chmod 777 /out
cat > /tmp/session.sh <<'EOS'
#!/usr/bin/env bash
set -u
export HOME=/tmp/u/home XDG_CONFIG_HOME=/tmp/u/config XDG_DATA_HOME=/tmp/u/data XDG_CACHE_HOME=/tmp/u/cache XDG_STATE_HOME=/tmp/u/state
export XDG_RUNTIME_DIR=/tmp/u/run
mkdir -p "$HOME" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_STATE_HOME" "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
unset WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER
eval "$(dbus-launch --sh-syntax)"
Xvfb :99 -screen 0 1280x800x24 -nolisten tcp > /tmp/xvfb.log 2>&1 &
for _ in $(seq 1 50); do [ -e /tmp/.X11-unix/X99 ] && break; sleep 0.1; done
export DISPLAY=:99 GDK_BACKEND=x11
openbox > /tmp/openbox.log 2>&1 &
sleep 0.5
echo "session: $(nproc) cpus, $(xdpyinfo 2>/dev/null | grep dimensions)" >> /out/runs.txt
cd /spike && bash docker/cases.sh "$@"
EOS
chmod 755 /tmp/session.sh
su ubuntu -s /bin/bash -c "/tmp/session.sh $*"
