#!/usr/bin/env bash
# What at-spi2-core adds to a CI image that already has the window tests' packages: two `apt-get -s` simulations
# against an empty package status (a bare image as far as apt can tell), no root, nothing installed. Inside WSL.
base=/var/tmp/spar-linux-blackbox-atspi/apt
mkdir -p "$base"
: >"$base/empty-status"
sim() { apt-get -s -o Dir::State::status="$base/empty-status" -o Debug::NoLocking=1 install --no-install-recommends "$@" 2>/dev/null | awk '/^Inst /{print $2}' | sort; }
window="libwebkitgtk-6.0-4 xvfb xauth dbus dbus-x11 fonts-dejavu-core"
sim $window >"$base/window.txt"
sim $window at-spi2-core >"$base/blackbox.txt"
echo "window-test set: $(wc -l <"$base/window.txt") packages; with at-spi2-core: $(wc -l <"$base/blackbox.txt")"
echo "added by at-spi2-core:"
comm -13 "$base/window.txt" "$base/blackbox.txt" | while read -r p; do
  apt-cache show --no-all-versions "$p" | awk -v p="$p" '/^Version:/{v=$2} /^Installed-Size:/{i=$2} /^Size:/{s=$2} END{printf "  %-28s %-20s download %7.1f KB  installed %6d KB\n", p, v, s/1024, i}'
done
echo "libatspi2.0-0t64 in the window-test set: $(grep -cx libatspi2.0-0t64 "$base/window.txt")"
