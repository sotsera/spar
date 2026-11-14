#!/usr/bin/env bash
# Usage: bash wsl/session.sh <run.sh arguments...>
# One measured run in its own private D-Bus session, so daemons activated by one run (at-spi, the portal and its GTK
# backend) never share libraries with the next. Afterwards stops only the session daemons that this run started.
set -u
source wsl/env.sh
names='^(at-spi-bus-laun|at-spi2-registr|dbus-daemon|xdg-desktop-por|xdg-document-po|xdg-permission-|gvfsd|dconf-service)$'
before=$(ps -u "$(id -u)" -o pid=,comm= | awk -v re="$names" '$2 ~ re {print $1}' | sort)
dbus-run-session -- bash wsl/run.sh "$@" 2>>"${ART:-$(cd ../.. && pwd)/artifacts/linux-memory}/session-stderr.log"
sleep 0.3
after=$(ps -u "$(id -u)" -o pid=,comm= | awk -v re="$names" '$2 ~ re {print $1}' | sort)
for pid in $(comm -13 <(echo "$before") <(echo "$after")); do kill "$pid" 2>/dev/null; done
