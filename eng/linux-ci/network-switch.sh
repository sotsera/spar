#!/bin/sh
# Runs a command in a network namespace of its own whose network can be switched off and on, for the page's online and
# offline events (tests/Spar.WindowTests/NetworkStatusTests.cs; decision "The Linux CI image and job; online and
# offline on Linux"). No root and no system setting: an unprivileged user namespace owns the network namespace, whose
# only link is the loopback, with a default route through it. GIO's network monitor reads the routes from netlink, so
# the default route is what "online" means to WebKit. A small loop in that user namespace removes the route on
# "offline" and adds it back on "online", read line by line from the FIFO named by SPAR_TEST_NETWORK_SWITCH; the
# command itself runs in a nested user namespace as the caller's own uid and gid, with no capability, so the web
# process's bwrap sandbox starts as it does outside. Nothing reaches the outside network from inside, which the window
# tests do not need (restore and builds run before, outside the switch).
#
#   eng/linux-ci/network-switch.sh dotnet test --project tests/Spar.WindowTests -c Debug --no-build
#
# Needs util-linux's unshare and iproute2's ip, and user namespaces allowed (as for bwrap).
set -eu

if [ "${SPAR_NETWORK_SWITCH_STAGE:-}" != inner ]; then
  # Stage 1, as the caller: a user namespace in which the caller is root, owning a new network namespace.
  SPAR_NETWORK_SWITCH_STAGE=inner SPAR_NETWORK_SWITCH_UID=$(id -u) SPAR_NETWORK_SWITCH_GID=$(id -g) \
    exec unshare --user --map-root-user --net -- sh "$0" "$@"
fi
unset SPAR_NETWORK_SWITCH_STAGE

# Stage 2, root of the user namespace (capabilities over this network namespace only).
ip link set lo up
ip route add default dev lo

shm=/dev/shm
[ -d "$shm" ] || shm=${TMPDIR:-/tmp}
folder=$(mktemp -d "$shm/spar-network-switch.XXXXXX")
fifo=$folder/control
mkfifo -m 600 "$fifo"

# The switch: one line per request, anything else ignored and named on standard error.
(
  while :; do
    while read -r request; do
      case $request in
        offline) ip route del default dev lo 2>/dev/null || true ;;
        online) ip route replace default dev lo ;;
        *) echo "network-switch: ignored '$request'" >&2 ;;
      esac
    done <"$fifo"
  done
) &
switch=$!

status=0
SPAR_TEST_NETWORK_SWITCH=$fifo unshare --user --map-user="$SPAR_NETWORK_SWITCH_UID" --map-group="$SPAR_NETWORK_SWITCH_GID" -- "$@" || status=$?
kill "$switch" 2>/dev/null || true
wait "$switch" 2>/dev/null || true
rm -rf "$folder"
exit "$status"
