#!/usr/bin/env bash
# Usage (inside WSL, from the repository's spikes/linux-integration folder): bash wsl/sync.sh
# Copies the repository's root build files and this spike's sources into a working copy on the WSL filesystem
# ($SPAR_WSL_WORK, default ~/spar-linux-integration; bin, obj and artifacts there are kept), writes an offline
# nuget.config whose only source is the Windows global packages folder, and builds the C helper.
set -eu
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
work=${SPAR_WSL_WORK:-$HOME/spar-linux-integration}
mkdir -p "$work/spikes/linux-integration"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Packages.props" "$repo/.editorconfig" "$work/"
(cd "$spike" && tar cf - --exclude=bin --exclude=obj Integ.Linux web wsl) | (cd "$work/spikes/linux-integration" && tar xf -)
if [ -z "${SPAR_WINDOWS_NUGET:-}" ]; then
  profile=$(cd /mnt/c && cmd.exe /c "echo %USERPROFILE%" 2>/dev/null | tr -d '\r')
  SPAR_WINDOWS_NUGET="$(wslpath "$profile")/.nuget/packages"
fi
cat >"$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="windows-global-packages" value="$SPAR_WINDOWS_NUGET" />
  </packageSources>
</configuration>
XML
cd "$work/spikes/linux-integration/wsl"
clang -O2 -Wall -o xtool xtool.c -lX11 -lXtst
echo "synced to $work"
# The Windows path of the repository, for win/wincap.ps1 (Windows-side capture of a WSLg window).
wslpath -w "$repo" >"$work/spikes/linux-integration/wsl/winrepo"
