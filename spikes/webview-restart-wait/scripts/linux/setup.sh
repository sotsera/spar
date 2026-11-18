#!/usr/bin/env bash
# Usage (inside WSL, no root): bash setup.sh. Downloads Xvfb, xkbcomp and their libraries that are not installed with
# apt-get download, unpacks them under $base/tools/root, and prepares the /usr/bin overlay for xvfb_start.
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
mkdir -p "$tools/debs" "$tools/root" "$tools/realbin" "$tools/usrbin"
cd "$tools/debs"
for pkg in xvfb xserver-common libxfont2 x11-xkb-utils xkb-data xauth libfontenc1 libxkbfile1 xfonts-base xfonts-encodings; do
  if ! dpkg -s "$pkg" >/dev/null 2>&1 && ! ls "$pkg"_*.deb >/dev/null 2>&1; then
    apt-get download "$pkg" 2>&1 | tail -1 || echo "MISSING $pkg"
  fi
done
for deb in *.deb; do dpkg -x "$deb" "$tools/root"; done
if [ ! -e "$tools/usrbin/xkbcomp" ]; then
  (cd "$tools/usrbin" && for f in /usr/bin/*; do ln -sfn "$tools/realbin/$(basename "$f")" "$(basename "$f")"; done && ln -sfn "$tools/root/usr/bin/xkbcomp" xkbcomp)
fi
ls "$tools/debs"
du -sh "$tools/debs" "$tools/root"
