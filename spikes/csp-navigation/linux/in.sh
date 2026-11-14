#!/usr/bin/env bash
# Usage (on the development machine, from anywhere): linux/in.sh <command...>
# Runs a command in the spike container as uid 1000 on Xvfb, from /work/spikes/csp-navigation.
export MSYS_NO_PATHCONV=1
exec docker exec -u 1000 -e HOME=/tmp/h -e DISPLAY=:99 -w /work/spikes/csp-navigation spar-csp-linux bash -c "mkdir -p /tmp/h; $*"
