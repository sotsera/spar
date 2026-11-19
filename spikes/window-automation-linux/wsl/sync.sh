#!/usr/bin/env bash
# Usage: bash wsl/sync.sh (inside WSL). Copies the repository's root build files, this spike's sources and the Windows
# spike's built page (spikes/window-automation/page/obj/dist) into the working copy, and prepares the bin overlay that
# lets Xvfb find the unpacked xkbcomp.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$here/../.." && pwd)
source "$here/wsl/common.sh"
mkdir -p "$spike" "$work/page"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Build.targets" "$repo/Directory.Packages.props" "$repo/.editorconfig" "$work/"
cat >"$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
XML
(cd "$here" && tar cf - --exclude=bin --exclude=obj --exclude=results Directory.Build.props Harness engine probes wsl 2>/dev/null) | (cd "$spike" && tar xf -)
rm -rf "$work/page/dist" && cp -r "$repo/spikes/window-automation/page/obj/dist" "$work/page/dist"
mkdir -p "$tools/realbin" "$tools/usrbin"
if [ ! -e "$tools/usrbin/xkbcomp" ]; then
  (cd "$tools/usrbin" && for f in /usr/bin/*; do ln -sfn "$tools/realbin/$(basename "$f")" "$(basename "$f")"; done && ln -sfn "$tools/root/usr/bin/xkbcomp" xkbcomp)
fi
echo "synced to $work"
