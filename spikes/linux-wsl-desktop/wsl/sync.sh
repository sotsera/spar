#!/usr/bin/env bash
# Usage (inside WSL, from the repository's spikes/linux-wsl-desktop folder): bash wsl/sync.sh
# Copies the repository's root build files, this spike's sources and the theme start-up spike's built frontend into a
# working copy on the WSL filesystem ($SPAR_WSL_WORK, default ~/spar-wsl-desktop; bin, obj and artifacts there are
# kept), writes an offline nuget.config that uses the Windows global packages folder as the only source, and builds
# the C helpers.
set -eu
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
work=${SPAR_WSL_WORK:-$HOME/spar-wsl-desktop}
mkdir -p "$work/spikes/linux-wsl-desktop" "$work/spikes/theme-startup/frontend"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Packages.props" "$repo/.editorconfig" "$work/"
(cd "$spike" && tar cf - --exclude=bin --exclude=obj Wsl.Linux web wsl) | (cd "$work/spikes/linux-wsl-desktop" && tar xf -)
rm -rf "$work/spikes/theme-startup/frontend/dist"
cp -r "$repo/spikes/theme-startup/frontend/dist" "$work/spikes/theme-startup/frontend/dist"
# The Windows user's global packages folder holds the .nupkg files of every package these projects need.
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
cd "$work/spikes/linux-wsl-desktop/wsl"
for tool in wcap xtest; do
  [ -f "$tool.c" ] && clang -O2 -Wall -o "$tool" "$tool.c" -lX11 -lz -ldl
done
echo "synced to $work (NuGet source $SPAR_WINDOWS_NUGET)"
