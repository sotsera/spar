#!/usr/bin/env bash
# Runs inside a throwaway test container, as root (started by docker/cases.sh):
#   in-container.sh <label> <session x11|wayland|none> <extras> <break> -- <app args...>
# 1. As root: applies <break> (a shell snippet that removes a piece of the system), and starts socat listeners on
#    /run/systemd/journal/socket and /dev/log unless <extras> contains nojournal / nodevlog.
# 2. As uid 1000 with private HOME and XDG folders: starts a session D-Bus, then Xvfb + openbox (x11) or headless
#    sway (wayland) or nothing (none), plus dunst if <extras> contains dunst; then the app, timed from launch to the
#    first new window; screenshot; dismissal with Return (X11); exit code; summary in /out/summary.txt.
set -u
label=$1 session=$2 extras=$3 brk=$4; shift 5
out=/out
mkdir -p "$out"; chmod 777 "$out"
bash -c "$brk" > "$out/break.txt" 2>&1
if [[ $extras != *nojournal* ]]; then
  mkdir -p /run/systemd/journal
  socat -u UNIX-RECV:/run/systemd/journal/socket,mode=666 OPEN:$out/journal.bin,creat,append &
fi
if [[ $extras != *nodevlog* ]]; then
  socat -u UNIX-RECV:/dev/log,mode=666 OPEN:$out/devlog.txt,creat,append &
fi
sleep 0.2
cat > /tmp/session.sh <<'EOS'
#!/usr/bin/env bash
set -u
label=$1 session=$2 extras=$3; shift 3
out=/out
export HOME=/tmp/u/home XDG_CONFIG_HOME=/tmp/u/config XDG_DATA_HOME=/tmp/u/data XDG_CACHE_HOME=/tmp/u/cache XDG_STATE_HOME=/tmp/u/state
export XDG_RUNTIME_DIR=/tmp/u/run
mkdir -p "$HOME" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_STATE_HOME" "$XDG_RUNTIME_DIR"; chmod 700 "$XDG_RUNTIME_DIR"
unset DISPLAY WAYLAND_DISPLAY
eval "$(dbus-launch --sh-syntax)"
count() { :; }
shot() { :; }
case $session in
  x11)
    Xvfb :99 -screen 0 1280x800x24 -nolisten tcp > /tmp/xvfb.log 2>&1 &
    for _ in $(seq 1 50); do [ -e /tmp/.X11-unix/X99 ] && break; sleep 0.1; done
    export DISPLAY=:99
    openbox > /tmp/openbox.log 2>&1 &
    sleep 0.5
    # openbox keeps the EWMH client list: every managed top-level window, whatever its name or class.
    # dunst's popups are override-redirect (not managed), so they are listed separately.
    count() { { xprop -root _NET_CLIENT_LIST 2>/dev/null | grep -o '0x[0-9a-f]*'; xdotool search --onlyvisible --classname dunst 2>/dev/null; } | sort; }
    shot() { xwd -root -silent | xwdtopnm 2>/dev/null | pnmtopng > "$1" 2>/dev/null; }
    ;;
  wayland)
    printf 'output HEADLESS-1 resolution 1280x800\ndefault_border normal\n' > /tmp/sway.conf
    WLR_BACKENDS=headless WLR_RENDERER=pixman WLR_LIBINPUT_NO_DEVICES=1 sway -c /tmp/sway.conf > /tmp/sway.log 2>&1 &
    for _ in $(seq 1 50); do [ -e "$XDG_RUNTIME_DIR/wayland-1" ] && break; sleep 0.1; done
    export WAYLAND_DISPLAY=wayland-1
    SWAYSOCK=$(ls "$XDG_RUNTIME_DIR"/sway-ipc.*.sock 2>/dev/null | head -1); export SWAYSOCK
    sleep 0.5
    count() { swaymsg -t get_tree 2>/dev/null | grep -o '"pid": [0-9]*' | sort; }
    shot() { grim "$1" 2>/dev/null; }
    ;;
esac
[ -n "${SPAR_DISPLAY:-}" ] && export DISPLAY=$SPAR_DISPLAY  # a display name whose server is gone
if [[ $extras == *dunst* ]]; then dunst > /tmp/dunst.log 2>&1 & sleep 0.5; fi
[[ $extras == *kde* ]] && export XDG_CURRENT_DESKTOP=KDE
[[ $extras == *cairo* ]] && export GSK_RENDERER=cairo  # GTK 4 without its GL renderer (software GPU default in the plan)
exe=/spike/Startup.Linux/bin/Release/net11.0/linux-x64/publish/Startup.Linux
before=$(count)
start=$(date +%s%N)
"$exe" --label "$label" --log "$out/app.jsonl" "$@" 2> "$out/stderr.txt" &
pid=$!
window_ms=""; wid=""; exited_at=""
while :; do
  now=$(date +%s%N)
  new=$(comm -13 <(echo "$before") <(count) | head -1)
  if [ -n "$new" ]; then window_ms=$(( (now - start) / 1000000 )); wid=$new; break; fi
  if ! kill -0 "$pid" 2>/dev/null; then
    [ -z "$exited_at" ] && exited_at=$now
    (( now - exited_at > 1500000000 )) && break   # a notification may still pop up after the app exits
  fi
  (( now - start > 20000000000 )) && break
  sleep 0.01
done
if [ -n "$wid" ]; then
  sleep 0.8
  shot "$out/shot.png"
  [ "$session" = x11 ] && xwininfo -id "${wid}" > "$out/window.txt" 2>&1
  if [ "$session" = x11 ] && kill -0 "$pid" 2>/dev/null; then
    xdotool windowactivate --sync "$wid" 2>/dev/null; sleep 0.2; xdotool key --clearmodifiers Return
  fi
else
  shot "$out/shot.png"
fi
for _ in $(seq 1 200); do kill -0 "$pid" 2>/dev/null || break; sleep 0.1; done
if kill -0 "$pid" 2>/dev/null; then kill "$pid"; fi
wait "$pid"; code=$?
total=$(( ($(date +%s%N) - start) / 1000000 ))
echo "label=$label session=$session exit=$code window_ms=${window_ms:-none} total_ms=$total" | tee "$out/summary.txt"
EOS
chmod 755 /tmp/session.sh
setpriv --reuid=1000 --regid=1000 --init-groups /tmp/session.sh "$label" "$session" "$extras" "$@"
sleep 0.3
pkill socat 2>/dev/null
# Show the captured journal datagram as text (binary-safe fields become readable).
[ -f "$out/journal.bin" ] && tr '\0' '?' < "$out/journal.bin" | cat -v > "$out/journal.txt"
chmod -R a+rw "$out"
