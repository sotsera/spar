#!/usr/bin/env bash
# Usage (inside WSL, from the repository's spikes/resource-usage folder): bash wsl/sync.sh
# Copies the root build files and this spike's Linux sources into the working copy ($SPAR_WORK/repo) and writes an
# offline nuget.config whose only source is the Windows global packages folder.
set -eu
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
source "$spike/wsl/env.sh"
work=$SPAR_WORK/repo
mkdir -p "$work/spikes/resource-usage"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Packages.props" "$repo/.editorconfig" "$work/"
cp "$spike/Directory.Build.props" "$spike/Directory.Packages.props" "$work/spikes/resource-usage/"
(cd "$spike" && tar cf - --exclude=bin --exclude=obj Res.Linux Shared www wsl tools) | (cd "$work/spikes/resource-usage" && tar xf -)
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
echo "synced to $work"
