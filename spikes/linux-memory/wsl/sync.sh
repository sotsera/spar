#!/usr/bin/env bash
# Usage (inside WSL, from the repository's spikes/linux-memory folder): bash wsl/sync.sh
# Copies the root build files, this spike's sources and the React spike's built bundle (as page data) into the
# working copy ($SPAR_WORK/repo), writes an offline nuget.config whose only source is the Windows global packages
# folder, and builds the C helpers.
set -eu
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
source "$spike/wsl/env.sh"
work=$SPAR_WORK/repo
mkdir -p "$work/spikes/linux-memory/web/assets"
cp "$repo/global.json" "$repo/Directory.Build.props" "$repo/Directory.Packages.props" "$repo/.editorconfig" "$work/"
(cd "$spike" && tar cf - --exclude=bin --exclude=obj --exclude=node_modules Mem.Linux web wsl docker electron) | (cd "$work/spikes/linux-memory" && tar xf -)
cp "$repo/spikes/linux-desktop/frontend-dist/assets/main-Bv9fbHoP.js" "$repo/spikes/linux-desktop/frontend-dist/assets/notes-DEkCPJI9.js" "$work/spikes/linux-memory/web/assets/"
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
cd "$work/spikes/linux-memory/wsl"
clang -O2 -Wall -o xt xt.c -lX11 -lXtst
clang -O2 -Wall -o floor-gtk3 floor.c $(pkg-config --cflags --libs gtk+-3.0)
clang -O2 -Wall -DSPAR_GTK4 -o floor-gtk4 floor.c $(pkg-config --cflags --libs gtk4)
wslpath -w "$repo" >"$work/spikes/linux-memory/wsl/winrepo"
echo "synced to $work"
