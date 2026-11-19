#!/usr/bin/env bash
# Usage (inside WSL Ubuntu-24.04, no root): bash wsl/setup.sh
# Prepares /var/tmp/spar-linux/window-automation-linux: Xvfb, xauth, xkbcomp and their missing libraries, and the 4.1
# WebDriver package, downloaded with apt-get download and unpacked under tools/ (nothing installed system-wide). The
# .NET SDK, NuGet packages and npm cache are the shared ones of eng/wsl (/var/tmp/spar-linux/cache).
set -euo pipefail
base=/var/tmp/spar-linux/window-automation-linux
tools=$base/tools
mkdir -p "$tools/debs" "$tools/root"
cd "$tools/debs"
for pkg in xvfb xserver-common libxfont2 x11-xkb-utils xkb-data xauth libfontenc1 libxkbfile1 xfonts-base xfonts-encodings \
           libunwind8 webkit2gtk-driver x11-utils xdotool libxdo3; do
  if ! dpkg -s "$pkg" >/dev/null 2>&1 && ! ls "$pkg"_*.deb >/dev/null 2>&1; then
    apt-get download "$pkg" 2>&1 | tail -1 || echo "MISSING $pkg"
  fi
done
for deb in *.deb; do dpkg -x "$deb" "$tools/root"; done
ls -la "$tools/debs"
du -sh "$tools/debs" "$tools/root"
